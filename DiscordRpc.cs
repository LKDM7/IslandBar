using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace IslandBar;

// API locale de Discord : seule source de l'identifiant du salon, absent des notifications Windows.
// L'app Discord est en "Public Client" (PKCE) : aucun secret n'est stocké ni distribué.
sealed class DiscordRpc(Action<string, string> onChannelLink, Action<Exception> log)
{
    const string ClientId = "1568641486164066477", Redirect = "http://127.0.0.1", TokenUrl = "https://discord.com/api/oauth2/token";
    const int OpHandshake = 0, OpFrame = 1, OpClose = 2, MaxFrameBytes = 1 << 20;

    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);
    static readonly HttpClient Http = CreateHttp();
    static readonly string TokenFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IslandBar", "discord.token");

    NamedPipeClientStream pipe;

    sealed class RpcError(string command, string detail) : Exception($"{command} refusé : {detail}");

    static HttpClient CreateHttp()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IslandBar/1.0");
        return client;
    }

    // Tourne sur le thread UI : chaque await y revient, donc aucun verrou.
    public async Task Run()
    {
        while (true)
        {
            try
            {
                pipe = await Connect();
                if (pipe != null) await Listen();
            }
            // shortcut: un refus coupe la fonction jusqu'au prochain lancement, donc la question revient à chaque démarrage ; mémoriser le refus si l'app est partagée
            catch (RpcError ex)
            {
                log(ex);
                return;
            }
            catch (Exception ex) { log(ex); } // Discord fermé ou réseau coupé : on réessaie sans redemander l'autorisation
            finally
            {
                pipe?.Dispose();
                pipe = null;
            }
            await Task.Delay(RetryDelay);
        }
    }

    static async Task<NamedPipeClientStream> Connect()
    {
        for (var n = 0; n < 10; n++)
        {
            // CurrentUserOnly : le jeton n'est envoyé qu'à un canal ouvert par ce compte Windows, pas à un autre compte du PC.
            var client = new NamedPipeClientStream(".", $"discord-ipc-{n}", PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await client.ConnectAsync(50);
                return client;
            }
            catch (Exception ex) when (ex is TimeoutException or UnauthorizedAccessException) { client.Dispose(); }
        }
        return null;
    }

    async Task Listen()
    {
        await Write(OpHandshake, new JsonObject { ["v"] = 1, ["client_id"] = ClientId });
        await Read(); // READY
        await Request("AUTHENTICATE", new JsonObject { ["access_token"] = await AccessToken() });
        await Request("SUBSCRIBE", [], "NOTIFICATION_CREATE");
        while (true)
        {
            var message = await Read();
            var (command, evt) = ((string)message["cmd"], (string)message["evt"]);
            var data = message["data"];
            // Le titre sert de nonce : la réponse dit ainsi à quelle notification appartient le salon.
            if (evt == "NOTIFICATION_CREATE")
                await Send("GET_CHANNEL", new JsonObject { ["channel_id"] = (string)data["channel_id"] }, (string)data["title"]);
            else if (command == "GET_CHANNEL" && evt != "ERROR" && Link((string)data["guild_id"], (string)data["id"]) is { } link)
                onChannelLink((string)message["nonce"], link);
        }
    }

    // Ces identifiants viennent de l'extérieur : seuls des chiffres peuvent finir dans une URL ouverte par le shell.
    public static string Link(string guild, string channel)
    {
        var server = string.IsNullOrEmpty(guild) ? "@me" : guild;
        return IsId(channel) && (server == "@me" || IsId(server)) ? $"discord://-/channels/{server}/{channel}" : null;
    }

    static bool IsId(string value) => !string.IsNullOrEmpty(value) && value.All(char.IsAsciiDigit);

    async Task<string> AccessToken()
    {
        if (LoadToken() is { } saved
            && await Exchange(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = saved, ["client_id"] = ClientId }) is { } refreshed)
            return refreshed;

        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var answer = await Request("AUTHORIZE", new JsonObject
        {
            ["client_id"] = ClientId,
            ["scopes"] = new JsonArray("rpc", "rpc.notifications.read"),
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        });
        return await Exchange(new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = (string)answer["code"],
            ["redirect_uri"] = Redirect,
            ["client_id"] = ClientId,
            ["code_verifier"] = verifier,
        }) ?? throw new RpcError("token", "code rejeté");
    }

    // Renvoie null si le jeton est mort. Une panne réseau ou un 5xx lève une exception : elle ne doit pas relancer l'autorisation.
    static async Task<string> Exchange(Dictionary<string, string> form)
    {
        using var response = await Http.PostAsync(TokenUrl, new FormUrlEncodedContent(form));
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized) return null;
        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        SaveToken((string)body["refresh_token"]); // Discord en donne un nouveau à chaque échange : l'ancien ne sert plus
        return (string)body["access_token"];
    }

    static string LoadToken()
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(TokenFile), null, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException) { return null; }
    }

    static void SaveToken(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TokenFile));
        File.WriteAllBytes(TokenFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
    }

    async Task<JsonNode> Request(string command, JsonObject args, string evt = null)
    {
        var nonce = Guid.NewGuid().ToString();
        await Send(command, args, nonce, evt);
        while (true)
        {
            var message = await Read();
            if ((string)message["nonce"] != nonce) continue;
            if ((string)message["evt"] == "ERROR") throw new RpcError(command, message["data"]?.ToJsonString());
            return message["data"];
        }
    }

    Task Send(string command, JsonObject args, string nonce, string evt = null) =>
        Write(OpFrame, new JsonObject { ["cmd"] = command, ["args"] = args, ["nonce"] = nonce, ["evt"] = evt });

    async Task Write(int op, JsonObject message)
    {
        var payload = Encoding.UTF8.GetBytes(message.ToJsonString());
        var frame = new byte[8 + payload.Length];
        BitConverter.TryWriteBytes(frame, op);
        BitConverter.TryWriteBytes(frame.AsSpan(4), payload.Length);
        payload.CopyTo(frame, 8);
        await pipe.WriteAsync(frame);
    }

    async Task<JsonNode> Read()
    {
        var header = new byte[8];
        await pipe.ReadExactlyAsync(header);
        var (op, length) = (BitConverter.ToInt32(header, 0), BitConverter.ToInt32(header, 4));
        if (length is < 0 or > MaxFrameBytes) throw new IOException($"trame RPC invalide ({length} octets)");
        var payload = new byte[length];
        await pipe.ReadExactlyAsync(payload);
        var message = JsonNode.Parse(payload);
        if (op == OpClose) throw new IOException($"Discord a fermé la connexion : {message?.ToJsonString()}");
        return message;
    }
}
