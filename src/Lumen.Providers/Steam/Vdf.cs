// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text;

namespace Lumen.Providers.Steam;

/// <summary>
/// A node of Valve's text KeyValues format (VDF/ACF). Keys keep file order; lookups ignore case because
/// Steam is not consistent (e.g. "LibraryFolders" vs "libraryfolders").
/// </summary>
public sealed class VdfObject
{
    private readonly List<KeyValuePair<string, object>> _properties = [];

    /// <summary>Values are either <see cref="string"/> or <see cref="VdfObject"/>.</summary>
    public IReadOnlyList<KeyValuePair<string, object>> Properties => _properties;

    public string? GetString(string key) => Find(key) as string;

    public VdfObject? GetObject(string key) => Find(key) as VdfObject;

    internal void Add(string key, object value) => _properties.Add(new(key, value));

    private object? Find(string key)
    {
        foreach (var (k, v) in _properties)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                return v;
            }
        }

        return null;
    }
}

public sealed class VdfParseException : FormatException
{
    public VdfParseException()
    {
    }

    public VdfParseException(string message)
        : base(message)
    {
    }

    public VdfParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public VdfParseException(string message, int line)
        : base($"{message} (line {line})")
    {
        Line = line;
    }

    public int Line { get; }
}

/// <summary>Dependency-free parser for the text KeyValues format. Read-only: Lumen never writes store files.</summary>
public static class VdfParser
{
    /// <summary>Parses a document; the returned object holds the top-level keys (e.g. "AppState").</summary>
    public static VdfObject Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var reader = new Reader(text);
        var root = ParseObjectBody(reader, isRoot: true);
        return root;
    }

    private static VdfObject ParseObjectBody(Reader reader, bool isRoot)
    {
        var obj = new VdfObject();
        while (true)
        {
            var token = reader.Next();
            if (token.Kind == TokenKind.End)
            {
                if (!isRoot)
                {
                    throw new VdfParseException("Unexpected end of file; missing '}'", reader.Line);
                }

                return obj;
            }

            if (token.Kind == TokenKind.Close)
            {
                if (isRoot)
                {
                    throw new VdfParseException("Unexpected '}'", reader.Line);
                }

                return obj;
            }

            if (token.Kind != TokenKind.String)
            {
                throw new VdfParseException("Expected a key", reader.Line);
            }

            var value = reader.Next();
            switch (value.Kind)
            {
                case TokenKind.String:
                    obj.Add(token.Text, value.Text);
                    break;
                case TokenKind.Open:
                    obj.Add(token.Text, ParseObjectBody(reader, isRoot: false));
                    break;
                default:
                    throw new VdfParseException($"Expected a value for key '{token.Text}'", reader.Line);
            }
        }
    }

    private enum TokenKind
    {
        String,
        Open,
        Close,
        End,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private sealed class Reader(string text)
    {
        private int _pos;

        public int Line { get; private set; } = 1;

        public Token Next()
        {
            while (true)
            {
                SkipWhitespaceAndComments();
                if (_pos >= text.Length)
                {
                    return new Token(TokenKind.End, string.Empty);
                }

                var c = text[_pos];
                switch (c)
                {
                    case '{':
                        _pos++;
                        return new Token(TokenKind.Open, "{");
                    case '}':
                        _pos++;
                        return new Token(TokenKind.Close, "}");
                    case '"':
                        return new Token(TokenKind.String, ReadQuoted());
                    case '[':
                        // Platform conditionals such as [$WIN32]: ignored.
                        SkipConditional();
                        continue;
                    default:
                        return new Token(TokenKind.String, ReadUnquoted());
                }
            }
        }

        private void SkipWhitespaceAndComments()
        {
            while (_pos < text.Length)
            {
                var c = text[_pos];
                if (c == '\n')
                {
                    Line++;
                    _pos++;
                }
                else if (char.IsWhiteSpace(c) || c == '﻿')
                {
                    _pos++;
                }
                else if (c == '/' && _pos + 1 < text.Length && text[_pos + 1] == '/')
                {
                    while (_pos < text.Length && text[_pos] != '\n')
                    {
                        _pos++;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        private string ReadQuoted()
        {
            var startLine = Line;
            _pos++; // opening quote
            var sb = new StringBuilder();
            while (_pos < text.Length)
            {
                var c = text[_pos++];
                switch (c)
                {
                    case '"':
                        return sb.ToString();
                    case '\\' when _pos < text.Length:
                        var next = text[_pos++];
                        switch (next)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case '\\': sb.Append('\\'); break;
                            case '"': sb.Append('"'); break;
                            default: sb.Append('\\').Append(next); break; // unknown escape: keep it verbatim
                        }

                        break;
                    case '\n':
                        Line++;
                        sb.Append(c);
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            throw new VdfParseException("Unterminated string", startLine);
        }

        private string ReadUnquoted()
        {
            var start = _pos;
            while (_pos < text.Length && !char.IsWhiteSpace(text[_pos]) && text[_pos] is not ('{' or '}' or '"'))
            {
                _pos++;
            }

            return text[start.._pos];
        }

        private void SkipConditional()
        {
            while (_pos < text.Length && text[_pos] != ']' && text[_pos] != '\n')
            {
                _pos++;
            }

            if (_pos < text.Length && text[_pos] == ']')
            {
                _pos++;
            }
        }
    }
}
