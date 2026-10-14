using Microsoft.Data.Sqlite;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Models;
using PersonalResourceWorkspace.Application.Resources;

namespace PersonalResourceWorkspace.Infrastructure.Persistence;

internal sealed class SqliteWorkspaceRepository(ISqliteConnectionFactory connectionFactory) : IWorkspaceRepository
{
    public async Task<IReadOnlyList<TagRecord>> GetTagsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, IsSystem FROM Tags ORDER BY IsSystem DESC, Name COLLATE NOCASE;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<TagRecord>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new TagRecord(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt64(2) == 1));
        return result;
    }

    public async Task SaveTagAsync(TagRecord tag, CancellationToken cancellationToken)
    {
        var name = tag.Name.Trim();
        if (name.Length == 0) throw new ArgumentException("Введите имя тэга.", nameof(tag));
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Tags(Id, Name, NormalizedName, IsSystem) VALUES($id,$name,$normalized,0)
            ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, NormalizedName=excluded.NormalizedName
            WHERE Tags.IsSystem=0;
            """;
        command.Parameters.AddWithValue("$id", tag.Id.ToString());
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$normalized", name.ToUpperInvariant());
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Системный тэг нельзя переименовать.");
    }

    public async Task DeleteTagAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Name FROM SavedSearches WHERE Id IN (SELECT SearchId FROM SavedSearchTags WHERE TagId=$id) ORDER BY Name;";
        command.Parameters.AddWithValue("$id", id.ToString());
        var usedBy = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) usedBy.Add(reader.GetString(0));
        if (usedBy.Count > 0)
            throw new InvalidOperationException($"Тэг используется поисками: {string.Join(", ", usedBy)}");
        command.CommandText = "SELECT IsSystem FROM Tags WHERE Id=$id;";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) != 0)
            throw new InvalidOperationException("Системный тэг нельзя удалить.");
        command.CommandText =
            """
            CREATE TEMP TABLE AffectedResources(Id TEXT PRIMARY KEY);
            INSERT INTO AffectedResources SELECT ResourceId FROM ResourceTags WHERE TagId=$id;
            DELETE FROM ResourceTags WHERE TagId=$id;
            DELETE FROM Tags WHERE Id=$id;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        // A resource that loses its last user tag enters Inbox unless it is in Trash.
        command.CommandText =
            """
            INSERT OR IGNORE INTO ResourceTags(ResourceId, TagId)
            SELECT r.Id, $inbox FROM Resources r JOIN AffectedResources a ON a.Id=r.Id
            WHERE NOT EXISTS (SELECT 1 FROM ResourceTags x WHERE x.ResourceId=r.Id AND x.TagId NOT IN ($inbox,$trash))
              AND NOT EXISTS (SELECT 1 FROM ResourceTags x WHERE x.ResourceId=r.Id AND x.TagId=$trash);
            """;
        command.Parameters.AddWithValue("$inbox", SystemTags.InboxId.ToString());
        command.Parameters.AddWithValue("$trash", SystemTags.TrashId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SavedSearchRecord>> GetSearchesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT s.Id,s.Name,s.ParentId,s.Glyph,s.Color,s.SortOrder,s.IsSystem,t.TagId
            FROM SavedSearches s LEFT JOIN SavedSearchTags t ON t.SearchId=s.Id
            ORDER BY s.SortOrder,s.rowid;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new Dictionary<Guid, (string Name, Guid? ParentId, string Glyph, string Color, int SortOrder, bool IsSystem, List<Guid> Tags)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = Guid.Parse(reader.GetString(0));
            if (!records.TryGetValue(id, out var value))
                value = (reader.GetString(1), reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)), reader.GetString(3), reader.GetString(4), reader.GetInt32(5), reader.GetInt64(6) == 1, []);
            if (!reader.IsDBNull(7)) value.Tags.Add(Guid.Parse(reader.GetString(7)));
            records[id] = value;
        }
        return records.Select(pair => new SavedSearchRecord(pair.Key, pair.Value.Name, pair.Value.ParentId,
            pair.Value.Glyph, pair.Value.Color, pair.Value.SortOrder, pair.Value.IsSystem, pair.Value.Tags)).ToArray();
    }

    public async Task SaveSearchAsync(SavedSearchRecord search, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(search.Name)) throw new ArgumentException("Введите имя поиска.", nameof(search));
        if (search.ParentId == search.Id) throw new InvalidOperationException("Поиск нельзя вложить в себя.");
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT IsSystem FROM SavedSearches WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", search.Id.ToString());
        var existing = await command.ExecuteScalarAsync(cancellationToken);
        var isSystem = existing is not null && Convert.ToInt64(existing, System.Globalization.CultureInfo.InvariantCulture) == 1;
        if (isSystem && (search.Name != (search.Id == SystemTags.TrashSearchId ? "Корзина" : "Входящие") ||
            search.ParentId is not null ||
            search.RequiredTagIds.Count != 1 ||
            search.RequiredTagIds[0] != (search.Id == SystemTags.TrashSearchId ? SystemTags.TrashId : SystemTags.InboxId)))
            throw new InvalidOperationException("Условия и положение системного поиска нельзя изменить.");
        if (search.ParentId is { } parent)
        {
            command.CommandText =
                """
                WITH RECURSIVE ancestors(Id,ParentId) AS (
                  SELECT Id,ParentId FROM SavedSearches WHERE Id=$parent
                  UNION ALL SELECT s.Id,s.ParentId FROM SavedSearches s JOIN ancestors a ON s.Id=a.ParentId
                ) SELECT EXISTS(SELECT 1 FROM ancestors WHERE Id=$id);
                """;
            command.Parameters.AddWithValue("$parent", parent.ToString());
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) == 1)
                throw new InvalidOperationException("Нельзя создать цикл вложенности поисков.");
        }
        command.CommandText =
            """
            INSERT INTO SavedSearches(Id,Name,ParentId,Glyph,Color,SortOrder,IsSystem)
            VALUES($id,$name,$parentId,$glyph,$color,$order,0)
            ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name,ParentId=excluded.ParentId,
              Glyph=excluded.Glyph,Color=excluded.Color,SortOrder=excluded.SortOrder;
            """;
        command.Parameters.AddWithValue("$name", search.Name.Trim());
        command.Parameters.AddWithValue("$parentId", (object?)search.ParentId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$glyph", search.Glyph);
        command.Parameters.AddWithValue("$color", search.Color);
        command.Parameters.AddWithValue("$order", search.SortOrder);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (!isSystem)
        {
            command.CommandText = "DELETE FROM SavedSearchTags WHERE SearchId=$id;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            command.CommandText = "INSERT INTO SavedSearchTags(SearchId,TagId) VALUES($id,$tag);";
            command.Parameters.Add("$tag", SqliteType.Text);
            foreach (var tagId in search.RequiredTagIds.Distinct())
            {
                command.Parameters["$tag"].Value = tagId.ToString();
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteSearchTreeAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SavedSearches WHERE Id=$id AND IsSystem=0;";
        command.Parameters.AddWithValue("$id", id.ToString());
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Системный поиск нельзя удалить.");
    }

    public async Task<IReadOnlyList<TaggedResourceRecord>> GetResourcesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT r.Id,r.Title,r.Description,r.Kind,r.Modified,r.Glyph,r.Address,
              r.VisualMode,r.VisualKind,r.VisualAssetPath,r.VisualOrigin,r.VisualSourceFingerprint,t.TagId
            FROM Resources r LEFT JOIN ResourceTags t ON t.ResourceId=r.Id ORDER BY r.rowid DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new Dictionary<Guid, (string Title,string Description,string Kind,string Modified,string Glyph,string Address,ResourceVisualDescriptor Visual,List<Guid> Tags)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = Guid.Parse(reader.GetString(0));
            if (!records.TryGetValue(id, out var value))
                value = (reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),
                    new ResourceVisualDescriptor(Enum.Parse<ResourceVisualMode>(reader.GetString(7)),Enum.Parse<ResourceVisualKind>(reader.GetString(8)),
                        reader.IsDBNull(9) ? null : reader.GetString(9),Enum.Parse<ResourceVisualOrigin>(reader.GetString(10)),reader.GetString(11)),[]);
            if (!reader.IsDBNull(12)) value.Tags.Add(Guid.Parse(reader.GetString(12)));
            records[id] = value;
        }
        return records.Select(pair => new TaggedResourceRecord(pair.Key,pair.Value.Title,pair.Value.Description,pair.Value.Kind,
            pair.Value.Modified,pair.Value.Glyph,pair.Value.Address,pair.Value.Visual,pair.Value.Tags)).ToArray();
    }

    public async Task SaveResourceAsync(TaggedResourceRecord resource, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO Resources VALUES($id,$title,$description,$kind,$modified,$glyph,$address,$mode,$visualKind,$asset,$origin,$fingerprint)
            ON CONFLICT(Id) DO UPDATE SET Title=excluded.Title,Description=excluded.Description,
              Kind=excluded.Kind,Modified=excluded.Modified,Glyph=excluded.Glyph,Address=excluded.Address,
              VisualMode=excluded.VisualMode,VisualKind=excluded.VisualKind,VisualAssetPath=excluded.VisualAssetPath,
              VisualOrigin=excluded.VisualOrigin,VisualSourceFingerprint=excluded.VisualSourceFingerprint;
            """;
        command.Parameters.AddWithValue("$id", resource.Id.ToString());
        command.Parameters.AddWithValue("$title", resource.Title);
        command.Parameters.AddWithValue("$description", resource.Description);
        command.Parameters.AddWithValue("$kind", resource.Kind);
        command.Parameters.AddWithValue("$modified", resource.Modified);
        command.Parameters.AddWithValue("$glyph", resource.Glyph);
        command.Parameters.AddWithValue("$address", resource.Address);
        command.Parameters.AddWithValue("$mode", resource.Visual.Mode.ToString());
        command.Parameters.AddWithValue("$visualKind", resource.Visual.Kind.ToString());
        command.Parameters.AddWithValue("$asset", (object?)resource.Visual.AssetPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$origin", resource.Visual.Origin.ToString());
        command.Parameters.AddWithValue("$fingerprint", resource.Visual.SourceFingerprint);
        await command.ExecuteNonQueryAsync(cancellationToken);
        command.CommandText = "DELETE FROM ResourceTags WHERE ResourceId=$id;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        command.CommandText = "INSERT INTO ResourceTags(ResourceId,TagId) VALUES($id,$tag);";
        command.Parameters.Add("$tag", SqliteType.Text);
        foreach (var tagId in resource.TagIds.Distinct())
        {
            command.Parameters["$tag"].Value = tagId.ToString();
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteResourceAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Resources WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, long>> GetResourceRanksAsync(Guid searchId, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ResourceId, Rank FROM SearchResourceRanks WHERE SearchId=$searchId;";
        command.Parameters.AddWithValue("$searchId", searchId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var ranks = new Dictionary<Guid, long>();
        while (await reader.ReadAsync(cancellationToken)) ranks.Add(Guid.Parse(reader.GetString(0)), reader.GetInt64(1));
        return ranks;
    }

    public async Task SaveResourceRanksAsync(Guid searchId, IReadOnlyList<Guid> orderedResourceIds, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM SearchResourceRanks WHERE SearchId=$searchId;";
        command.Parameters.AddWithValue("$searchId", searchId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
        long nextRank = 0;
        command.CommandText = "INSERT INTO SearchResourceRanks(SearchId,ResourceId,Rank) VALUES($searchId,$resourceId,$rank) " +
                              "ON CONFLICT(SearchId,ResourceId) DO UPDATE SET Rank=excluded.Rank;";
        command.Parameters.Add("$resourceId", SqliteType.Text);
        command.Parameters.Add("$rank", SqliteType.Integer);
        foreach (var id in orderedResourceIds.Distinct())
        {
            command.Parameters["$resourceId"].Value = id.ToString();
            command.Parameters["$rank"].Value = nextRank++;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
