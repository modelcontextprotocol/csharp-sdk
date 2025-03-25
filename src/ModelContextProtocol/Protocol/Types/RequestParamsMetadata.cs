// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Metadata related to the request.
/// </summary>
public class RequestParamsMetadata
{
    /// <summary>
    /// If specified, the caller is requesting out-of-band progress notifications for this request (as represented by notifications/progress). The value of this parameter is an opaque token that will be attached to any subsequent notifications. The receiver is not obligated to provide these notifications.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("progressToken")]
    public object ProgressToken { get; set; } = default!;
}
