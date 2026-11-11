using System.Buffers;
using System.Text.Json;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Inspects the top-level members of a JSON-RPC message that is fed in pieces, in constant memory.
/// </summary>
/// <remarks>
/// <para>
/// A transport uses this for a message it does not keep whole, such as one that exceeds the maximum
/// message size. The scanner records the top-level <c>id</c> and whether <c>method</c>, <c>result</c>,
/// or <c>error</c> is present, so the transport can tell a request it should answer from a response to
/// one of its own requests. The position of <c>id</c> is not fixed: this SDK writes it after <c>params</c>,
/// so it can follow megabytes of argument data.
/// </para>
/// <para>
/// This is best effort, not a validator. It tracks only nesting, strings, and escapes. Anything it
/// does not expect at the top level ends the scan, keeping what was found so far. The first top-level
/// <c>id</c> wins. A key longer than <see cref="MaxKeyLength"/> bytes is treated as an unknown key, and
/// an id value longer than <see cref="MaxIdLength"/> bytes is not recovered.
/// </para>
/// </remarks>
internal sealed class JsonRpcFrameScanner
{
    /// <summary>A key longer than this, quotes included, is not one of the keys of interest, even escaped.</summary>
    private const int MaxKeyLength = 64;

    /// <summary>An id value longer than this is not recovered.</summary>
    private const int MaxIdLength = 1024;

    private readonly byte[] _key = new byte[MaxKeyLength];
    private readonly byte[] _idValue = new byte[MaxIdLength];

    private State _state;
    private int _keyLength;
    private bool _keyOverflow;
    private Key _currentKey;
    private int _idValueLength;
    private bool _escape;
    private bool _inString;
    private int _depth;
    private RequestId _id;
    private bool _hasId;

    private enum State
    {
        BeforeRoot,
        ExpectKey,
        InKey,
        ExpectColon,
        ExpectValue,
        InIdString,
        InIdLiteral,
        SkipValue,
        SkipLiteral,
        AfterValue,
        Done,
    }

    private enum Key
    {
        Other,
        Id,
        Method,
        Result,
        Error,
    }

    /// <summary>Gets a value indicating whether the scan has ended; further input is ignored.</summary>
    public bool IsComplete => _state == State.Done;

    /// <summary>Gets a value indicating whether a top-level <c>method</c> member was seen.</summary>
    public bool HasMethod { get; private set; }

    /// <summary>Gets a value indicating whether a top-level <c>result</c> or <c>error</c> member was seen.</summary>
    public bool HasResultOrError { get; private set; }

    /// <summary>Starts a new scan.</summary>
    public void Reset()
    {
        _state = State.BeforeRoot;
        _keyLength = 0;
        _keyOverflow = false;
        _currentKey = Key.Other;
        _idValueLength = 0;
        _escape = false;
        _inString = false;
        _depth = 0;
        _id = default;
        _hasId = false;
        HasMethod = false;
        HasResultOrError = false;
    }

    /// <summary>Feeds the next bytes of the message.</summary>
    public void Feed(in ReadOnlySequence<byte> bytes)
    {
        foreach (ReadOnlyMemory<byte> segment in bytes)
        {
            if (IsComplete)
            {
                return;
            }

            Feed(segment.Span);
        }
    }

    /// <summary>Feeds the next bytes of the message.</summary>
    public void Feed(ReadOnlySpan<byte> bytes)
    {
        int i = 0;
        while (i < bytes.Length && _state != State.Done)
        {
            byte b = bytes[i];
            switch (_state)
            {
                case State.BeforeRoot:
                    if (b == (byte)'{')
                    {
                        _state = State.ExpectKey;
                    }
                    else if (!IsWhiteSpace(b))
                    {
                        Stop();
                    }

                    i++;
                    break;

                case State.ExpectKey:
                    if (b == (byte)'"')
                    {
                        _key[0] = b;
                        _keyLength = 1;
                        _keyOverflow = false;
                        _escape = false;
                        _state = State.InKey;
                    }
                    else if (!IsWhiteSpace(b))
                    {
                        // '}' ends the object; anything else is not a key.
                        Stop();
                    }

                    i++;
                    break;

                case State.InKey:
                    AppendKey(b);
                    if (_escape)
                    {
                        _escape = false;
                    }
                    else if (b == (byte)'\\')
                    {
                        _escape = true;
                    }
                    else if (b == (byte)'"')
                    {
                        _currentKey = _keyOverflow ? Key.Other : ClassifyKey(_key.AsSpan(0, _keyLength));
                        _state = State.ExpectColon;
                    }

                    i++;
                    break;

                case State.ExpectColon:
                    if (b == (byte)':')
                    {
                        _state = State.ExpectValue;
                    }
                    else if (!IsWhiteSpace(b))
                    {
                        Stop();
                    }

                    i++;
                    break;

                case State.ExpectValue:
                    i++;
                    if (IsWhiteSpace(b))
                    {
                        break;
                    }

                    switch (_currentKey)
                    {
                        case Key.Method:
                            HasMethod = true;
                            break;
                        case Key.Result or Key.Error:
                            HasResultOrError = true;
                            break;
                        case Key.Id when !_hasId:
                            if (b is (byte)'{' or (byte)'[')
                            {
                                // An object or array is not a valid JSON-RPC id.
                                Stop();
                                return;
                            }

                            _idValue[0] = b;
                            _idValueLength = 1;
                            _escape = false;
                            _state = b == (byte)'"' ? State.InIdString : State.InIdLiteral;
                            continue;
                    }

                    _escape = false;
                    if (b == (byte)'"')
                    {
                        _inString = true;
                        _depth = 0;
                        _state = State.SkipValue;
                    }
                    else if (b is (byte)'{' or (byte)'[')
                    {
                        _inString = false;
                        _depth = 1;
                        _state = State.SkipValue;
                    }
                    else
                    {
                        _state = State.SkipLiteral;
                    }

                    break;

                case State.InIdString:
                    if (!AppendIdValue(b))
                    {
                        break;
                    }

                    if (_escape)
                    {
                        _escape = false;
                    }
                    else if (b == (byte)'\\')
                    {
                        _escape = true;
                    }
                    else if (b == (byte)'"')
                    {
                        FinishId();
                    }

                    i++;
                    break;

                case State.InIdLiteral:
                    if (b is (byte)',' or (byte)'}' || IsWhiteSpace(b))
                    {
                        // Leave the delimiter for AfterValue.
                        FinishId();
                        break;
                    }

                    if (AppendIdValue(b))
                    {
                        i++;
                    }

                    break;

                case State.SkipValue:
                    i = SkipValue(bytes, i);
                    break;

                case State.SkipLiteral:
                    if (b == (byte)',')
                    {
                        _state = State.ExpectKey;
                    }
                    else if (b == (byte)'}')
                    {
                        Stop();
                    }
                    else if (IsWhiteSpace(b))
                    {
                        _state = State.AfterValue;
                    }

                    i++;
                    break;

                case State.AfterValue:
                    if (b == (byte)',')
                    {
                        _state = State.ExpectKey;
                    }
                    else if (!IsWhiteSpace(b))
                    {
                        // '}' ends the object; anything else is malformed.
                        Stop();
                    }

                    i++;
                    break;
            }
        }
    }

    /// <summary>Ends the input and returns the top-level id, if one was found.</summary>
    public bool TryGetId(out RequestId id)
    {
        if (_state == State.InIdLiteral)
        {
            // A number at the very end of the input has no delimiter after it.
            ParseId();
        }

        id = _id;
        return _hasId;
    }

    /// <summary>
    /// Skips the body of a value that is not the id. Inside a string, it jumps to the next quote or
    /// backslash with a vectorized search, so a multi-megabyte argument is not walked byte by byte.
    /// </summary>
    private int SkipValue(ReadOnlySpan<byte> bytes, int i)
    {
        while (i < bytes.Length)
        {
            if (_inString)
            {
                if (_escape)
                {
                    _escape = false;
                    i++;
                    continue;
                }

                int next = bytes.Slice(i).IndexOfAny((byte)'"', (byte)'\\');
                if (next < 0)
                {
                    return bytes.Length;
                }

                i += next;
                if (bytes[i] == (byte)'\\')
                {
                    _escape = true;
                    i++;
                    continue;
                }

                // Closing quote.
                _inString = false;
                i++;
                if (_depth == 0)
                {
                    _state = State.AfterValue;
                    return i;
                }

                continue;
            }

            switch (bytes[i++])
            {
                case (byte)'"':
                    _inString = true;
                    break;
                case (byte)'{' or (byte)'[':
                    _depth++;
                    break;
                case (byte)'}' or (byte)']':
                    if (--_depth == 0)
                    {
                        _state = State.AfterValue;
                        return i;
                    }

                    break;
            }
        }

        return i;
    }

    private void AppendKey(byte b)
    {
        if (_keyLength < MaxKeyLength)
        {
            _key[_keyLength++] = b;
        }
        else
        {
            _keyOverflow = true;
        }
    }

    private bool AppendIdValue(byte b)
    {
        if (_idValueLength >= MaxIdLength)
        {
            Stop();
            return false;
        }

        _idValue[_idValueLength++] = b;
        return true;
    }

    private void FinishId()
    {
        ParseId();
        _state = State.AfterValue;
    }

    private void ParseId()
    {
        try
        {
            var reader = new Utf8JsonReader(_idValue.AsSpan(0, _idValueLength), isFinalBlock: true, state: default);
            if (!reader.Read())
            {
                return;
            }

            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    _id = new RequestId(reader.GetString()!);
                    _hasId = true;
                    break;

                case JsonTokenType.Number when reader.TryGetInt64(out long number):
                    _id = new RequestId(number);
                    _hasId = true;
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Not a valid string or integer, or a string with an invalid escape sequence: no id.
        }
    }

    private void Stop() => _state = State.Done;

    /// <summary>Classifies a quoted key, unescaping it first if needed (for example <c>"id"</c>).</summary>
    private static Key ClassifyKey(ReadOnlySpan<byte> quotedKey)
    {
        if (quotedKey.IndexOf((byte)'\\') < 0)
        {
            return
                quotedKey.SequenceEqual("\"id\""u8) ? Key.Id :
                quotedKey.SequenceEqual("\"method\""u8) ? Key.Method :
                quotedKey.SequenceEqual("\"result\""u8) ? Key.Result :
                quotedKey.SequenceEqual("\"error\""u8) ? Key.Error :
                Key.Other;
        }

        try
        {
            var reader = new Utf8JsonReader(quotedKey, isFinalBlock: true, state: default);
            if (!reader.Read() || reader.TokenType != JsonTokenType.String)
            {
                return Key.Other;
            }

            return
                reader.ValueTextEquals("id"u8) ? Key.Id :
                reader.ValueTextEquals("method"u8) ? Key.Method :
                reader.ValueTextEquals("result"u8) ? Key.Result :
                reader.ValueTextEquals("error"u8) ? Key.Error :
                Key.Other;
        }
        catch (JsonException)
        {
            return Key.Other;
        }
    }

    private static bool IsWhiteSpace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
}
