package com.frappe.platform.infrastructure.mail;

import java.util.Set;
import org.jsoup.Jsoup;
import org.jsoup.nodes.Element;
import org.jsoup.nodes.Node;
import org.jsoup.nodes.TextNode;
import org.jsoup.select.NodeTraversor;
import org.jsoup.select.NodeVisitor;

/**
 * Derives the plain-text part of a mail from its rendered HTML body, so every mail is sent as {@code
 * multipart/alternative} (clients and spam filters expect a text part) without a second template per mail. jsoup parses
 * and decodes; this class only decides where lines break: a blank line between blocks, one line per {@code <br>} and
 * list item, and a link's address after its text.
 */
final class PlainTextAlternative {

    private static final Set<String> PARAGRAPHS =
            Set.of("p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "table", "blockquote", "section");

    private static final Set<String> LINES = Set.of("li", "tr");

    private PlainTextAlternative() {}

    /**
     * Converts an HTML fragment to text.
     *
     * @param html the rendered body, for example {@code <p>Your code is 123456.</p>}
     * @return the text, trimmed, with {@code \n} line breaks
     */
    static String of(String html) {
        var body = Jsoup.parseBodyFragment(html).body();
        body.select("style, script").remove();
        var text = new Writer();
        NodeTraversor.traverse(text, body);
        return text.toString();
    }

    private static final class Writer implements NodeVisitor {

        private final StringBuilder out = new StringBuilder();

        private int pendingBreaks;

        @Override
        public void head(Node node, int depth) {
            if (node instanceof TextNode textNode) {
                write(textNode.text());
            } else if (node instanceof Element element) {
                var tag = element.normalName();
                if (tag.equals("br")) {
                    pendingBreaks = Math.min(pendingBreaks + 1, 2);
                } else if (PARAGRAPHS.contains(tag)) {
                    breakLines(2);
                } else if (LINES.contains(tag)) {
                    breakLines(1);
                    if (tag.equals("li")) {
                        write("- ");
                    }
                }
            }
        }

        @Override
        public void tail(Node node, int depth) {
            if (!(node instanceof Element element)) {
                return;
            }
            var tag = element.normalName();
            if (tag.equals("a")) {
                var href = element.attr("href");
                if (!href.isBlank() && !href.equals(element.text())) {
                    write(" (" + href + ")");
                }
            } else if (PARAGRAPHS.contains(tag)) {
                breakLines(2);
            } else if (LINES.contains(tag)) {
                breakLines(1);
            }
        }

        private void breakLines(int count) {
            pendingBreaks = Math.max(pendingBreaks, count);
        }

        // Whitespace collapses as in HTML; breaks are written lazily, so none leads or trails the text.
        private void write(String text) {
            var collapsed = text.replaceAll("\\s+", " ");
            if (atLineStart()) {
                collapsed = collapsed.stripLeading();
            }
            if (collapsed.isEmpty()) {
                return;
            }
            if (pendingBreaks > 0 && !out.isEmpty()) {
                stripTrailingSpaces();
                out.append("\n".repeat(pendingBreaks));
                collapsed = collapsed.stripLeading();
            }
            pendingBreaks = 0;
            out.append(collapsed);
        }

        private boolean atLineStart() {
            return out.isEmpty() || pendingBreaks > 0 || out.charAt(out.length() - 1) == '\n';
        }

        private void stripTrailingSpaces() {
            while (!out.isEmpty() && out.charAt(out.length() - 1) == ' ') {
                out.setLength(out.length() - 1);
            }
        }

        @Override
        public String toString() {
            stripTrailingSpaces();
            return out.toString();
        }
    }
}
