using System.IO;
using System.IO.Pipes;

namespace IslandBar;

// IslandBar.exe --notify [--icon image.png] "texte" : le second processus passe le message à l'île déjà lancée, puis s'arrête.
// CurrentUserOnly : un autre compte du PC ne peut pas écrire dans l'île.
static class ScriptNotes
{
    const string Pipe = "IslandBar";
    const int MaxLength = 300;
    const int MaxPath = 260;

    public static int Send(string text, string icon = null)
    {
        if (icon?.Length > MaxPath) icon = null;
        try
        {
            using var pipe = new NamedPipeClientStream(".", Pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(1000);
            using var writer = new StreamWriter(pipe);
            writer.Write($"{icon}\0{(text.Length > MaxLength ? text[..MaxLength] : text)}"); // l'île ne lit pas plus loin : le reste ferait échouer l'envoi
            return 0;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException) { return 1; } // île non lancée
    }

    // "image\0texte", ou "\0texte" sans image.
    public static (string Text, string Icon) Read(string message)
    {
        var split = message.IndexOf('\0');
        return (message[(split + 1)..].ReplaceLineEndings(" ").Trim(), split > 0 ? message[..split] : null);
    }

    // Une erreur (canal encore tenu par une île qui se ferme, client coupé en pleine écriture) est notée, puis l'écoute
    // reprend : sans ça, plus aucun --notify n'arriverait jusqu'au prochain redémarrage.
    public static async Task Listen(Action<string, string> received, Action<Exception> failed)
    {
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(Pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe);
                var buffer = new char[MaxPath + 1 + MaxLength];
                // Un client connecté qui n'écrit rien tiendrait le seul canal : passé ce délai, on l'abandonne.
                using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var length = await reader.ReadBlockAsync(buffer, patience.Token);
                var (text, icon) = Read(new string(buffer, 0, length));
                if (text != "") received(text, icon);
            }
            catch (Exception ex)
            {
                failed(ex);
                await Task.Delay(TimeSpan.FromSeconds(1)); // une erreur qui se répète ne doit pas faire tourner la boucle à vide
            }
        }
    }
}
