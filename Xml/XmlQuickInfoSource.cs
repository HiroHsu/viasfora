﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace Winterdom.Viasfora.Xml {
  internal class XmlQuickInfoSource : IQuickInfoSource {
    private ITextBuffer textBuffer;
    private XmlQuickInfoSourceProvider provider;

    public XmlQuickInfoSource(ITextBuffer buffer, XmlQuickInfoSourceProvider provider) {
      this.textBuffer = buffer;
      this.provider = provider;
    }
    public void Dispose() {
    }
    public void AugmentQuickInfoSession(
        IQuickInfoSession session, IList<object> quickInfoContent,
        out ITrackingSpan applicableToSpan) {
      applicableToSpan = null;
      SnapshotPoint? subjectTriggerPoint =
        session.GetTriggerPoint(textBuffer.CurrentSnapshot);
      if ( !subjectTriggerPoint.HasValue ) {
        return;
      }
      ITextSnapshot currentSnapshot = subjectTriggerPoint.Value.Snapshot;
      SnapshotSpan querySpan = new SnapshotSpan(subjectTriggerPoint.Value, 0);

      var tagAggregator = GetAggregator(session);
      TextExtent extent = FindExtentAtPoint(subjectTriggerPoint);

      if ( CheckForPrefixTag(tagAggregator, extent.Span) ) {
        String text = extent.Span.GetText();
        String url = FindNSUri(extent.Span, GetDocText(extent.Span));
        applicableToSpan = currentSnapshot.CreateTrackingSpan(
          extent.Span, SpanTrackingMode.EdgeInclusive
        );
        quickInfoContent.Add(CreateInfoText(text, url));
      }
    }

    private FrameworkElement CreateInfoText(String xmlns, String url) {
        var textBlock = new TextBlock();
        textBlock.Inlines.AddRange(new Inline[] {
          new Bold(new Run("Prefix: ")),
          new Run(xmlns),
          new LineBreak(),
          new Bold(new Run("Namespace: ")),
          new Hyperlink(new Run(url))
        });
        return textBlock;
    }

    private TextExtent FindExtentAtPoint(SnapshotPoint? subjectTriggerPoint) {
      ITextStructureNavigator navigator =
        provider.NavigatorService.GetTextStructureNavigator(textBuffer);
      TextExtent extent = navigator.GetExtentOfWord(subjectTriggerPoint.Value);
      return extent;
    }

    // Ugly method, but not sure how else to grab this
    // short of parsing the document up to the element we're on.
    private String FindNSUri(SnapshotSpan span, String docText) {
      String subtext = FindMinTextToParse(span, docText);
      using ( StringReader sr = new StringReader(subtext) ) {
        XmlReaderSettings settings = new XmlReaderSettings();
        settings.ConformanceLevel = ConformanceLevel.Fragment;
        using ( XmlReader reader = XmlReader.Create(sr, settings) ) {
          String thisPrefix = span.GetText();
          String lastUriForPrefix = ReadXmlUntilEnd(reader, thisPrefix);
          return String.IsNullOrEmpty(lastUriForPrefix) ? "unknown" : lastUriForPrefix;
        }
      }
    }

    private static String ReadXmlUntilEnd(XmlReader reader, String thisPrefix) {
      String lastUriForPrefix = null;
      try {
        while ( reader.Read() ) {
          if ( reader.Prefix == thisPrefix ) {
            lastUriForPrefix = reader.NamespaceURI;
          } else if ( reader.NodeType == XmlNodeType.Element ) {
            for ( int i = 0; i < reader.AttributeCount; i++ ) {
              reader.MoveToAttribute(i);
              if ( reader.Prefix == thisPrefix ) {
                lastUriForPrefix = reader.NamespaceURI;
              }
            }
          }
        }
      } catch {
      }
      return lastUriForPrefix;
    }

    private static String FindMinTextToParse(SnapshotSpan span, String docText) {
      String subtext = docText;
      int endElem = docText.IndexOf('>', span.Span.End);
      if ( endElem > 0 && endElem < docText.Length - 1 ) {
        subtext = docText.Substring(0, endElem + 1);
      }
      return subtext;
    }
    private String GetDocText(SnapshotSpan span) {
      return span.Snapshot.GetText();
    }
    private bool CheckForPrefixTag(
        ITagAggregator<IClassificationTag> tagAggregator,
        SnapshotSpan span) {
      String text = span.GetText();
      if ( text.StartsWith("<") || text.Contains(":") ) {
        return false;
      }
      var firstMatch = from tagSpan in tagAggregator.GetTags(span)
                       let tagName = tagSpan.Tag.ClassificationType.Classification
                       where tagName == Constants.XML_PREFIX
                       select tagSpan;
      return firstMatch.FirstOrDefault() != null;
    }

    private ITagAggregator<IClassificationTag> GetAggregator(IQuickInfoSession session) {
      return provider.AggregatorFactory.CreateTagAggregator<IClassificationTag>(
        session.TextView
      );
    }

  }

}
