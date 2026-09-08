namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// The exception thrown when a skill file's content does not match its manifest entry, or when a file is read
/// that the manifest does not list.
/// </summary>
/// <remarks>
/// A mismatch means the content is not what the entry described. It may be corrupted, tampered with, or stale
/// because the skill was updated after the entry was fetched. In all cases the content must not be used. To
/// recover from staleness, fetch a fresh entry with <c>skills/get</c> and proceed from its manifest; because the
/// manifest changed, any approval bound to the previous one is revoked.
/// </remarks>
public sealed class SkillVerificationException : McpException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SkillVerificationException"/> class.
    /// </summary>
    public SkillVerificationException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SkillVerificationException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public SkillVerificationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SkillVerificationException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public SkillVerificationException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}
