using System.IO;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace IslandBar;

// L'API WinRT ne donne que le texte d'une notification : l'image n'existe que dans la base de Windows.
static class Avatars
{
    const string Sqlite = "winsqlite3.dll";
    const int SQLITE_OK = 0, SQLITE_ROW = 100, SQLITE_OPEN_READONLY = 1;

    [DllImport(Sqlite)] static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string file, out nint db, int flags, nint vfs);
    [DllImport(Sqlite)] static extern int sqlite3_close(nint db);
    [DllImport(Sqlite)] static extern int sqlite3_prepare_v2(nint db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out nint statement, nint tail);
    [DllImport(Sqlite)] static extern int sqlite3_bind_int64(nint statement, int index, long value);
    [DllImport(Sqlite)] static extern int sqlite3_step(nint statement);
    [DllImport(Sqlite)] static extern nint sqlite3_column_text(nint statement, int column);
    [DllImport(Sqlite)] static extern int sqlite3_finalize(nint statement);

    static readonly string Database = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Notifications\wpndatabase.db");

    public static string PathFor(uint notificationId) => PathIn(Field("Payload", notificationId));

    public static string TagFor(uint notificationId) => Field("Tag", notificationId);


    static string Field(string column, uint id)
    {
        if (sqlite3_open_v2(Database, out var db, SQLITE_OPEN_READONLY, 0) != SQLITE_OK)
        {
            sqlite3_close(db);
            return null;
        }
        try
        {
            if (sqlite3_prepare_v2(db, $"SELECT {column} FROM Notification WHERE Id = ?", -1, out var statement, 0) != SQLITE_OK) return null;
            try
            {
                sqlite3_bind_int64(statement, 1, id);
                return sqlite3_step(statement) == SQLITE_ROW ? Marshal.PtrToStringUTF8(sqlite3_column_text(statement, 0)) : null;
            }
            finally { sqlite3_finalize(statement); }
        }
        finally { sqlite3_close(db); }
    }

    public static string PathIn(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return null;
        var source = XDocument.Parse(payload).Descendants("image").FirstOrDefault()?.Attribute("src")?.Value;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || !uri.IsFile) return null;
        // L'outil Capture écrit le lecteur échappé (file:///C%3A%5C...) : LocalPath garde alors une barre devant « C: ».
        return Local(uri.LocalPath.TrimStart('/'));
    }

    // Lettre de lecteur exigée : ouvrir un partage réseau enverrait l'identité Windows au serveur distant.
    public static string Local(string path) =>
        path is { Length: > 2 } && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/' ? Path.GetFullPath(path) : null;
}
