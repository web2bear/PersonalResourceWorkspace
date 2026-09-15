using Microsoft.Data.Sqlite;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Models;
using System.Globalization;

namespace PersonalResourceWorkspace.Infrastructure.Persistence;

internal sealed class SqliteCollectionRepository(ISqliteConnectionFactory connectionFactory) : ICollectionRepository
{
    public async Task<IReadOnlyList<CollectionRecord>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Name, Glyph, Color, SortOrder, IsDefault, IsTrash, VisualAssetPath FROM Collections ORDER BY SortOrder ASC, Name ASC;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CollectionRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CollectionRecord(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt64(5) != 0,
                reader.GetInt64(6) != 0,
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return result;
    }

    public async Task SaveAsync(CollectionRecord collection, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var previousName = await GetNameAsync(connection, transaction, collection.Id, cancellationToken);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO Collections (Id, Name, Glyph, Color, SortOrder, IsDefault, IsTrash, VisualAssetPath)
                    VALUES ($id, $name, $glyph, $color, $sortOrder, $isDefault, $isTrash, $visualAssetPath)
                    ON CONFLICT(Id) DO UPDATE SET
                        Name = $name,
                        Glyph = $glyph,
                        Color = $color,
                        SortOrder = $sortOrder,
                        IsDefault = $isDefault,
                        IsTrash = $isTrash,
                        VisualAssetPath = $visualAssetPath;
                    """;
                command.Parameters.AddWithValue("$id", collection.Id.ToString());
                command.Parameters.AddWithValue("$name", collection.Name);
                command.Parameters.AddWithValue("$glyph", collection.Glyph);
                command.Parameters.AddWithValue("$color", collection.Color);
                command.Parameters.AddWithValue("$sortOrder", collection.SortOrder);
                command.Parameters.AddWithValue("$isDefault", collection.IsDefault ? 1 : 0);
                command.Parameters.AddWithValue("$isTrash", collection.IsTrash ? 1 : 0);
                command.Parameters.AddWithValue("$visualAssetPath", (object?)collection.VisualAssetPath ?? DBNull.Value);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            if (previousName is not null &&
                !string.Equals(previousName, collection.Name, StringComparison.Ordinal))
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE Resources SET Collection = $newName WHERE Collection = $oldName;";
                command.Parameters.AddWithValue("$newName", collection.Name);
                command.Parameters.AddWithValue("$oldName", previousName);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task DeleteAsync(Guid id, Guid reassignToId, CancellationToken cancellationToken)
    {
        if (id == reassignToId)
        {
            throw new InvalidOperationException("Нельзя удалить коллекцию без коллекции для переноса ресурсов.");
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await IsDefaultAsync(connection, transaction, id, cancellationToken))
            {
                throw new InvalidOperationException("Нельзя удалить коллекцию по умолчанию.");
            }

            if (await IsTrashAsync(connection, transaction, id, cancellationToken))
            {
                throw new InvalidOperationException("Нельзя удалить корзину.");
            }

            var collectionName = await GetNameAsync(connection, transaction, id, cancellationToken)
                ?? throw new InvalidOperationException("Коллекция не найдена.");
            var fallbackName = await GetNameAsync(connection, transaction, reassignToId, cancellationToken)
                ?? throw new InvalidOperationException("Коллекция для переноса ресурсов не найдена.");

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE Resources SET Collection = $fallback WHERE Collection = $name;";
                command.Parameters.AddWithValue("$fallback", fallbackName);
                command.Parameters.AddWithValue("$name", collectionName);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM Collections WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", id.ToString());
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<string?> GetNameAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Name FROM Collections WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    private static async Task<bool> IsDefaultAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT IsDefault FROM Collections WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull && Convert.ToInt64(result, CultureInfo.InvariantCulture) != 0;
    }

    private static async Task<bool> IsTrashAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT IsTrash FROM Collections WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull && Convert.ToInt64(result, CultureInfo.InvariantCulture) != 0;
    }
}
