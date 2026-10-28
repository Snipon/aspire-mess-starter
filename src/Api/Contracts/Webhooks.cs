namespace Api.Contracts;

/// <summary>Body of a content-changed webhook. Only the id of the affected item is used.</summary>
public sealed record ContentWebhookPayload(string Id);

/// <summary>Outcome of a webhook-triggered index sync.</summary>
public sealed record ContentSyncResult(string Id, string Action);

/// <summary>Outcome of a full reindex.</summary>
public sealed record ReindexResult(int Indexed);
