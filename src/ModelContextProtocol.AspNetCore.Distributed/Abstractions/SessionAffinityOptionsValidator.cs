// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Options;

namespace ModelContextProtocol.AspNetCore.Distributed.Abstractions;

/// <summary>
/// Validator for <see cref="SessionAffinityOptions"/> that ensures configuration is valid.
/// Uses compile-time code generation for AOT compatibility.
/// The source generator will automatically validate data annotations on the options class.
/// </summary>
[OptionsValidator]
internal sealed partial class SessionAffinityOptionsValidator
    : IValidateOptions<SessionAffinityOptions>
{ }
