using System;
using System.Collections.Generic;

namespace Tero.Postino.Domain.Models;

public sealed class MailJob
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString("N");
    public string To { get; init; } = string.Empty;
    public string? Subject { get; init; }
    public string? HtmlBody { get; init; }
    public string? PlainTextBody { get; init; }
    public Dictionary<string, object>? TemplateModel { get; init; }
}
