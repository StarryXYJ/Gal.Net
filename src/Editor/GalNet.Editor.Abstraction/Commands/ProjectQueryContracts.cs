using System.Text.Json;

namespace GalNet.Editor.Abstraction.Commands;

public sealed record ProjectFileQuery(string? Root = null, string? Glob = null, string? Extension = null,
    string? Kind = null, int Offset = 0, int? Limit = null);
public sealed record ProjectIdentityQuery(string Id, string? Kind = null, string? GroupId = null,
    int Offset = 0, int? Limit = null);
public sealed record GroupSelector(string? Id = null, string? File = null);
public sealed record ProjectFileItem(string File, string Kind, long Size, string? ResourceId);
public sealed record ProjectObjectItem(string Kind, string? Id, string File, string JsonPointer,
    JsonElement Content, string? GroupId = null);
public sealed record ProjectGroupItem(string? Id, string? Name, string File, bool Referenced, bool Exists);
public sealed record ProjectReferenceItem(string File, string JsonPointer, string Match, string Evidence);
public sealed record RawDocumentQuery(string Scope, JsonElement? Content);
public sealed record QueryPage<T>(string Scope, int Total, int Offset, int Returned, bool HasMore,
    IReadOnlyList<T> Items, bool Complete);
