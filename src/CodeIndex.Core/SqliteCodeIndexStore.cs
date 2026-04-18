using Microsoft.Data.Sqlite;

namespace CodeIndex.Core;

public sealed class SqliteCodeIndexStore
{
    public async Task WriteAsync(string databasePath, CodeIndexSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        await using var connection = await OpenConnectionAsync(fullPath, cancellationToken);
        await CreateSchemaAsync(connection, cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await InsertMetaAsync(connection, transaction, snapshot.Meta, cancellationToken);
        await InsertFilesAsync(connection, transaction, snapshot.Files, cancellationToken);
        await InsertSymbolsAsync(connection, transaction, snapshot.Symbols, cancellationToken);
        await InsertEdgesAsync(connection, transaction, snapshot.Edges, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CodeIndexSnapshot> ReadAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        await using var connection = await OpenConnectionAsync(databasePath, cancellationToken);
        var meta = await ReadMetaAsync(connection, cancellationToken);
        var files = await ReadFilesAsync(connection, cancellationToken);
        var symbols = await ReadSymbolsAsync(connection, cancellationToken);
        var edges = await ReadEdgesAsync(connection, cancellationToken);

        return new CodeIndexSnapshot(meta, files, symbols, edges);
    }

    public async Task<CodeIndexMeta> ReadMetaAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        await using var connection = await OpenConnectionAsync(databasePath, cancellationToken);
        return await ReadMetaAsync(connection, cancellationToken);
    }

    public async Task<IReadOnlyList<FileRecord>> ReadFilesAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        await using var connection = await OpenConnectionAsync(databasePath, cancellationToken);
        return await ReadFilesAsync(connection, cancellationToken);
    }

    public async Task<int> CountSymbolsAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        await using var connection = await OpenConnectionAsync(databasePath, cancellationToken);
        return await CountRowsAsync(connection, "symbols", cancellationToken);
    }

    public async Task<int> CountEdgesAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        await using var connection = await OpenConnectionAsync(databasePath, cancellationToken);
        return await CountRowsAsync(connection, "edges", cancellationToken);
    }

    public async Task<FileRecord?> GetFileByPathAsync(string databasePath, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var connection = await OpenConnectionAsync(databasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, path, project_name, language, hash, summary
FROM files
WHERE path = $path COLLATE NOCASE
LIMIT 1;
""";
        command.Parameters.AddWithValue("$path", path);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadFile(reader) : null;
    }

    public async Task<IReadOnlyList<SymbolRecord>> FindSymbolsAsync(
        string databasePath,
        string query,
        string? kind,
        string? accessibility,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        await using var connection = await OpenConnectionAsync(databasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, name, qualified_name, kind, file_id, start_line, start_column, end_line, end_column, signature, summary, parent_id, accessibility, is_static, is_abstract, is_virtual, is_override
FROM symbols
WHERE (name = $query COLLATE NOCASE OR qualified_name = $query COLLATE NOCASE OR qualified_name LIKE '%' || $query || '%' COLLATE NOCASE)
  AND ($kind IS NULL OR kind = $kind COLLATE NOCASE)
  AND ($accessibility IS NULL OR accessibility = $accessibility COLLATE NOCASE);
""";
        command.Parameters.AddWithValue("$query", query);
        command.Parameters.AddWithValue("$kind", (object?)NormalizeOptional(kind) ?? DBNull.Value);
        command.Parameters.AddWithValue("$accessibility", (object?)NormalizeOptional(accessibility) ?? DBNull.Value);

        return await ReadSymbolRowsAsync(command, cancellationToken);
    }

    public async Task<SymbolRecord?> GetSymbolAsync(string databasePath, string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        await using var connection = await OpenConnectionAsync(databasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, name, qualified_name, kind, file_id, start_line, start_column, end_line, end_column, signature, summary, parent_id, accessibility, is_static, is_abstract, is_virtual, is_override
FROM symbols
WHERE id = $query OR qualified_name = $query COLLATE NOCASE
LIMIT 1;
""";
        command.Parameters.AddWithValue("$query", query);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSymbol(reader) : null;
    }

    public async Task<IReadOnlyList<SymbolRecord>> GetChildrenAsync(
        string databasePath,
        string query,
        string? kind,
        string? accessibility,
        CancellationToken cancellationToken = default)
    {
        var parent = await GetSymbolAsync(databasePath, query, cancellationToken);

        if (parent is null)
        {
            return Array.Empty<SymbolRecord>();
        }

        await using var connection = await OpenConnectionAsync(databasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT s.id, s.name, s.qualified_name, s.kind, s.file_id, s.start_line, s.start_column, s.end_line, s.end_column, s.signature, s.summary, s.parent_id, s.accessibility, s.is_static, s.is_abstract, s.is_virtual, s.is_override
FROM edges e
JOIN symbols s ON s.id = e.to_id
WHERE e.type = $contains
  AND e.from_id = $parentId
  AND ($kind IS NULL OR s.kind = $kind COLLATE NOCASE)
  AND ($accessibility IS NULL OR s.accessibility = $accessibility COLLATE NOCASE);
""";
        command.Parameters.AddWithValue("$contains", EdgeTypes.Contains);
        command.Parameters.AddWithValue("$parentId", parent.Id);
        command.Parameters.AddWithValue("$kind", (object?)NormalizeOptional(kind) ?? DBNull.Value);
        command.Parameters.AddWithValue("$accessibility", (object?)NormalizeOptional(accessibility) ?? DBNull.Value);

        return await ReadSymbolRowsAsync(command, cancellationToken);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync(string databasePath, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
CREATE TABLE meta (
    schema_version TEXT NOT NULL,
    tool_version TEXT NOT NULL,
    repo_name TEXT NOT NULL,
    generated_at_utc TEXT NOT NULL,
    source_root TEXT NOT NULL,
    input_path TEXT NOT NULL,
    input_kind TEXT NOT NULL
);

CREATE TABLE files (
    id TEXT PRIMARY KEY,
    path TEXT NOT NULL UNIQUE,
    project_name TEXT NOT NULL,
    language TEXT NOT NULL,
    hash TEXT NOT NULL,
    summary TEXT NOT NULL
);

CREATE TABLE symbols (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    qualified_name TEXT NOT NULL,
    kind TEXT NOT NULL,
    file_id TEXT NOT NULL,
    start_line INTEGER NOT NULL,
    start_column INTEGER NOT NULL,
    end_line INTEGER NOT NULL,
    end_column INTEGER NOT NULL,
    signature TEXT NOT NULL,
    summary TEXT NOT NULL,
    parent_id TEXT NULL,
    accessibility TEXT NOT NULL,
    is_static INTEGER NOT NULL,
    is_abstract INTEGER NOT NULL,
    is_virtual INTEGER NOT NULL,
    is_override INTEGER NOT NULL
);

CREATE TABLE edges (
    type TEXT NOT NULL,
    from_id TEXT NOT NULL,
    to_id TEXT NOT NULL,
    PRIMARY KEY (type, from_id, to_id)
);

CREATE INDEX idx_files_path ON files(path);
CREATE INDEX idx_symbols_name ON symbols(name);
CREATE INDEX idx_symbols_qualified_name ON symbols(qualified_name);
CREATE INDEX idx_symbols_parent_id ON symbols(parent_id);
CREATE INDEX idx_edges_from_id ON edges(from_id);
""";

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertMetaAsync(SqliteConnection connection, SqliteTransaction transaction, CodeIndexMeta meta, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO meta(schema_version, tool_version, repo_name, generated_at_utc, source_root, input_path, input_kind)
VALUES ($schemaVersion, $toolVersion, $repoName, $generatedAtUtc, $sourceRoot, $inputPath, $inputKind);
""";
        command.Parameters.AddWithValue("$schemaVersion", meta.SchemaVersion);
        command.Parameters.AddWithValue("$toolVersion", meta.ToolVersion);
        command.Parameters.AddWithValue("$repoName", meta.RepoName);
        command.Parameters.AddWithValue("$generatedAtUtc", meta.GeneratedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$sourceRoot", meta.SourceRoot);
        command.Parameters.AddWithValue("$inputPath", meta.InputPath);
        command.Parameters.AddWithValue("$inputKind", meta.InputKind);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertFilesAsync(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<FileRecord> files, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO files(id, path, project_name, language, hash, summary)
VALUES ($id, $path, $projectName, $language, $hash, $summary);
""";
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var path = command.Parameters.Add("$path", SqliteType.Text);
        var projectName = command.Parameters.Add("$projectName", SqliteType.Text);
        var language = command.Parameters.Add("$language", SqliteType.Text);
        var hash = command.Parameters.Add("$hash", SqliteType.Text);
        var summary = command.Parameters.Add("$summary", SqliteType.Text);

        foreach (var file in files)
        {
            id.Value = file.Id;
            path.Value = file.Path;
            projectName.Value = file.ProjectName;
            language.Value = file.Language;
            hash.Value = file.Hash;
            summary.Value = file.Summary;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertSymbolsAsync(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<SymbolRecord> symbols, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO symbols(id, name, qualified_name, kind, file_id, start_line, start_column, end_line, end_column, signature, summary, parent_id, accessibility, is_static, is_abstract, is_virtual, is_override)
VALUES ($id, $name, $qualifiedName, $kind, $fileId, $startLine, $startColumn, $endLine, $endColumn, $signature, $summary, $parentId, $accessibility, $isStatic, $isAbstract, $isVirtual, $isOverride);
""";
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var qualifiedName = command.Parameters.Add("$qualifiedName", SqliteType.Text);
        var kind = command.Parameters.Add("$kind", SqliteType.Text);
        var fileId = command.Parameters.Add("$fileId", SqliteType.Text);
        var startLine = command.Parameters.Add("$startLine", SqliteType.Integer);
        var startColumn = command.Parameters.Add("$startColumn", SqliteType.Integer);
        var endLine = command.Parameters.Add("$endLine", SqliteType.Integer);
        var endColumn = command.Parameters.Add("$endColumn", SqliteType.Integer);
        var signature = command.Parameters.Add("$signature", SqliteType.Text);
        var summary = command.Parameters.Add("$summary", SqliteType.Text);
        var parentId = command.Parameters.Add("$parentId", SqliteType.Text);
        var accessibility = command.Parameters.Add("$accessibility", SqliteType.Text);
        var isStatic = command.Parameters.Add("$isStatic", SqliteType.Integer);
        var isAbstract = command.Parameters.Add("$isAbstract", SqliteType.Integer);
        var isVirtual = command.Parameters.Add("$isVirtual", SqliteType.Integer);
        var isOverride = command.Parameters.Add("$isOverride", SqliteType.Integer);

        foreach (var symbol in symbols)
        {
            id.Value = symbol.Id;
            name.Value = symbol.Name;
            qualifiedName.Value = symbol.QualifiedName;
            kind.Value = symbol.Kind;
            fileId.Value = symbol.FileId;
            startLine.Value = symbol.Range.StartLine;
            startColumn.Value = symbol.Range.StartColumn;
            endLine.Value = symbol.Range.EndLine;
            endColumn.Value = symbol.Range.EndColumn;
            signature.Value = symbol.Signature;
            summary.Value = symbol.Summary;
            parentId.Value = (object?)symbol.ParentId ?? DBNull.Value;
            accessibility.Value = symbol.Accessibility;
            isStatic.Value = symbol.IsStatic ? 1 : 0;
            isAbstract.Value = symbol.IsAbstract ? 1 : 0;
            isVirtual.Value = symbol.IsVirtual ? 1 : 0;
            isOverride.Value = symbol.IsOverride ? 1 : 0;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertEdgesAsync(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<EdgeRecord> edges, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO edges(type, from_id, to_id)
VALUES ($type, $fromId, $toId);
""";
        var type = command.Parameters.Add("$type", SqliteType.Text);
        var fromId = command.Parameters.Add("$fromId", SqliteType.Text);
        var toId = command.Parameters.Add("$toId", SqliteType.Text);

        foreach (var edge in edges)
        {
            type.Value = edge.Type;
            fromId.Value = edge.From;
            toId.Value = edge.To;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<CodeIndexMeta> ReadMetaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT schema_version, tool_version, repo_name, generated_at_utc, source_root, input_path, input_kind
FROM meta
LIMIT 1;
""";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("SQLite index is missing metadata.");
        }

        return new CodeIndexMeta(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            DateTimeOffset.Parse(reader.GetString(3)),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6));
    }

    private static async Task<IReadOnlyList<FileRecord>> ReadFilesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, path, project_name, language, hash, summary
FROM files
ORDER BY path;
""";

        var results = new List<FileRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadFile(reader));
        }

        return results;
    }

    private static async Task<IReadOnlyList<SymbolRecord>> ReadSymbolsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, name, qualified_name, kind, file_id, start_line, start_column, end_line, end_column, signature, summary, parent_id, accessibility, is_static, is_abstract, is_virtual, is_override
FROM symbols
ORDER BY qualified_name;
""";

        return await ReadSymbolRowsAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyList<EdgeRecord>> ReadEdgesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT type, from_id, to_id
FROM edges
ORDER BY type, from_id, to_id;
""";

        var results = new List<EdgeRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new EdgeRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return results;
    }

    private static async Task<int> CountRowsAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = tableName switch
        {
            "symbols" => "SELECT COUNT(*) FROM symbols;",
            "edges" => "SELECT COUNT(*) FROM edges;",
            _ => throw new InvalidOperationException($"Unsupported SQLite table count target: {tableName}")
        };

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<IReadOnlyList<SymbolRecord>> ReadSymbolRowsAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var results = new List<SymbolRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadSymbol(reader));
        }

        return results;
    }

    private static FileRecord ReadFile(SqliteDataReader reader)
    {
        return new FileRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5));
    }

    private static SymbolRecord ReadSymbol(SqliteDataReader reader)
    {
        return new SymbolRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            new TextRangeRecord(reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8)),
            reader.GetString(9),
            reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetString(12),
            reader.GetInt32(13) != 0,
            reader.GetInt32(14) != 0,
            reader.GetInt32(15) != 0,
            reader.GetInt32(16) != 0);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}