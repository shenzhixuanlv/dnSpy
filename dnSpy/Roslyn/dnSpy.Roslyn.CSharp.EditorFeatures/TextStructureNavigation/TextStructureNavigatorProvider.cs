// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Composition;
using dnSpy.Roslyn.EditorFeatures.Editor;
using dnSpy.Roslyn.EditorFeatures.Host;
using dnSpy.Roslyn.EditorFeatures.TextStructureNavigation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;

namespace dnSpy.Roslyn.CSharp.EditorFeatures.TextStructureNavigation {
	[Export(typeof(ITextStructureNavigatorProvider))]
	[ContentType(ContentTypeNames.CSharpContentType)]
	class TextStructureNavigatorProvider : AbstractTextStructureNavigatorProvider {
		[ImportingConstructor]
		internal TextStructureNavigatorProvider(ITextStructureNavigatorSelectorService selectorService,
			IContentTypeRegistryService contentTypeService,
			IWaitIndicator waitIndicator)
			: base(selectorService, contentTypeService, waitIndicator) { }

		protected override bool ShouldSelectEntireTriviaFromStart(SyntaxTrivia trivia) => trivia.IsRegularOrDocComment();

		protected override bool IsWithinNaturalLanguage(SyntaxToken token, int position) {
			switch (token.Kind()) {
			case SyntaxKind.StringLiteralToken:
			case SyntaxKind.Utf8StringLiteralToken:
				// This, in combination with the override of GetExtentOfWordFromToken() below, treats the closing
				// quote as a separate token.  This maintains behavior with VS2013.
				return !IsAtClosingQuote(token, position);

			case SyntaxKind.SingleLineRawStringLiteralToken:
			case SyntaxKind.MultiLineRawStringLiteralToken:
			case SyntaxKind.Utf8SingleLineRawStringLiteralToken:
			case SyntaxKind.Utf8MultiLineRawStringLiteralToken: {
				// Like with normal string literals, treat the closing quotes as as the end of the string so that
				// navigation ends there and doesn't go past them.
				var end = GetStartOfRawStringLiteralEndDelimiter(token);
				return position < end;
			}

			case SyntaxKind.CharacterLiteralToken:
				// Before the ' is considered outside the character
				return position != token.SpanStart;

			case SyntaxKind.InterpolatedStringTextToken:
			case SyntaxKind.XmlTextLiteralToken:
				return true;
			}

			return false;
		}

		static int GetStartOfRawStringLiteralEndDelimiter(SyntaxToken token) {
			var text = token.ToString();
			var start = 0;
			var end = text.Length;

			if (token.Kind() is SyntaxKind.Utf8MultiLineRawStringLiteralToken or SyntaxKind.Utf8SingleLineRawStringLiteralToken) {
				// Skip past the u8 suffix
				end -= "u8".Length;
			}

			while (start < end && text[start] == '"')
				start++;

			while (end > start && text[end - 1] == '"')
				end--;

			return token.SpanStart + end;
		}

		static bool IsAtClosingQuote(SyntaxToken token, int position) {
			switch (token.Kind()) {
			case SyntaxKind.StringLiteralToken:
				return position == token.Span.End - 1 && token.Text[token.Text.Length - 1] == '"';
			case SyntaxKind.Utf8StringLiteralToken:
				if (position != token.Span.End - 3)
					return false;
				int length = token.Text.Length;
				if (length < 3)
					return false;
				char c = token.Text[length - 3];
				if (c != '"')
					return false;
				c = token.Text[length - 2];
				if (c != 'U' && c != 'u')
					return false;
				c = token.Text[length - 1];
				return c == '8';
			default:
				throw new ArgumentOutOfRangeException(nameof(token));
			}
		}

		protected override TextExtent GetExtentOfWordFromToken(SyntaxToken token, SnapshotPoint position) {
			if (token.Kind() is SyntaxKind.StringLiteralToken or SyntaxKind.Utf8StringLiteralToken &&
				IsAtClosingQuote(token, position.Position)) {
				// Special case to treat the closing quote of a string literal as a separate token.  This allows the
				// cursor to stop during word navigation (Ctrl+LeftArrow, etc.) immediately before AND after the
				// closing quote, just like it did in VS2013 and like it currently does for interpolated strings.
				var span = new Span(position.Position, 1);
				return new TextExtent(new SnapshotSpan(position.Snapshot, span), isSignificant: true);
			}

			if (token.Kind() is
				SyntaxKind.SingleLineRawStringLiteralToken or
				SyntaxKind.MultiLineRawStringLiteralToken or
				SyntaxKind.Utf8SingleLineRawStringLiteralToken or
				SyntaxKind.Utf8MultiLineRawStringLiteralToken) {
				var delimiterStart = GetStartOfRawStringLiteralEndDelimiter(token);
				return new TextExtent(new SnapshotSpan(position.Snapshot, Span.FromBounds(delimiterStart, token.Span.End)), isSignificant: true);
			}

			return base.GetExtentOfWordFromToken(token, position);
		}
	}
}
