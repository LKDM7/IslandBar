using System.IO;
using System.IO.Pipes;

namespace IslandBar;

// IslandBar.exe --notify "texte" : le second processus passe le texte à l'île déjà lancée, puis s'arrête.
// CurrentUserOnly : un autre compte du PC ne peut pas écrire dans l'île.
static class ScriptNotes
{
    const string Pipe = "IslandBar";
    const int MaxLength = 300;

    public static int Send(string text)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(1000);
            using var writer = new StreamWriter(pipe);
            writer.Write(text);
            return 0;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException) { return 1; } // île non lancée
    }

    public static async Task Listen(Action<string> received)
    {
        while (true)
        {
            using var pipe = new NamedPipeServerStream(Pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync();
            using var reader = new StreamReader(pipe);
            var buffer = new char[MaxLength];
            var length = await reader.ReadBlockAsync(buffer);
            var text = new string(buffer, 0, length).ReplaceLineEndings(" ").Trim();
            if (text != "") received(text);
        }
    }
}
