using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Models;

namespace PersonalResourceWorkspace.Infrastructure.Persistence;

internal sealed class SqliteResourceRepository(ISqliteConnectionFactory connectionFactory) : IResourceRepository
{
    public async Task<IReadOnlyList<ResourceRecord>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, Description, Kind, Collection, Modified, Glyph, IsFavorite, Address, VisualMode, VisualKind, VisualAssetPath, VisualOrigin, VisualSourceFingerprint FROM Resources ORDER BY rowid DESC;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ResourceRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ResourceRecord(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt64(7) != 0,
                reader.GetString(8),
                new ResourceVisualDescriptor(
                    ParseEnum(reader.GetString(9), ResourceVisualMode.Auto),
                    ParseEnum(reader.GetString(10), ResourceVisualKind.TypeFallback),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    ParseEnum(reader.GetString(12), ResourceVisualOrigin.Fallback),
                    reader.GetString(13))));
        }
        return result;
    }

    public async Task SaveAsync(ResourceRecord resource, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Resources (Id, Title, Description, Kind, Collection, Modified, Glyph, IsFavorite, Address, VisualMode, VisualKind, VisualAssetPath, VisualOrigin, VisualSourceFingerprint) VALUES ($id, $title, $description, $kind, $collection, $modified, $glyph, $favorite, $address, $visualMode, $visualKind, $visualAssetPath, $visualOrigin, $visualSourceFingerprint) ON CONFLICT(Id) DO UPDATE SET Title=$title, Description=$description, Kind=$kind, Collection=$collection, Modified=$modified, Glyph=$glyph, IsFavorite=$favorite, Address=$address, VisualMode=$visualMode, VisualKind=$visualKind, VisualAssetPath=$visualAssetPath, VisualOrigin=$visualOrigin, VisualSourceFingerprint=$visualSourceFingerprint;";
        command.Parameters.AddWithValue("$id", resource.Id.ToString());
        command.Parameters.AddWithValue("$title", resource.Title);
        command.Parameters.AddWithValue("$description", resource.Description);
        command.Parameters.AddWithValue("$kind", resource.Kind);
        command.Parameters.AddWithValue("$collection", resource.Collection);
        command.Parameters.AddWithValue("$modified", resource.Modified);
        command.Parameters.AddWithValue("$glyph", resource.Glyph);
        command.Parameters.AddWithValue("$favorite", resource.IsFavorite ? 1 : 0);
        command.Parameters.AddWithValue("$address", resource.Address);
        command.Parameters.AddWithValue("$visualMode", resource.Visual.Mode.ToString());
        command.Parameters.AddWithValue("$visualKind", resource.Visual.Kind.ToString());
        command.Parameters.AddWithValue("$visualAssetPath", (object?)resource.Visual.AssetPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$visualOrigin", resource.Visual.Origin.ToString());
        command.Parameters.AddWithValue("$visualSourceFingerprint", resource.Visual.SourceFingerprint);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Resources WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteByCollectionAsync(string collectionName, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Resources WHERE Collection = $collection;";
        command.Parameters.AddWithValue("$collection", collectionName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) ? parsed : fallback;
}
