package com.frappe.platform.infrastructure.i18n;

import java.io.IOException;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Properties;
import org.springframework.core.io.Resource;
import org.springframework.core.io.support.PathMatchingResourcePatternResolver;

/** Loads message catalogs from the classpath, reading every file as UTF-8. */
final class MessageCatalogs {

    private static final String FILE_PREFIX = "messages_";
    private static final String FILE_SUFFIX = ".properties";

    private MessageCatalogs() {}

    /**
     * Loads every catalog matching a location pattern. The module is the directory holding the file, the locale comes
     * from its name ({@code messages_pt.properties} is Portuguese).
     *
     * @param locationPattern a Spring resource pattern such as {@code classpath*:i18n/*}{@code /messages_*.properties}
     * @return the catalogs found, possibly none
     * @throws UnreadableMessageCatalogException if the pattern cannot be resolved or a file cannot be read
     */
    static List<MessageCatalog> load(String locationPattern) {
        var catalogs = new ArrayList<MessageCatalog>();
        for (var resource : resolve(locationPattern)) {
            catalogs.add(read(resource));
        }
        return List.copyOf(catalogs);
    }

    private static Resource[] resolve(String locationPattern) {
        try {
            return new PathMatchingResourcePatternResolver().getResources(locationPattern);
        } catch (IOException e) {
            throw new UnreadableMessageCatalogException(
                    "Cannot list message catalogs at '%s'".formatted(locationPattern), e);
        }
    }

    private static MessageCatalog read(Resource resource) {
        var location = resource.getDescription();
        try (var reader = new InputStreamReader(resource.getInputStream(), StandardCharsets.UTF_8)) {
            var properties = new Properties();
            properties.load(reader);
            Map<String, String> messages = new HashMap<>();
            properties.stringPropertyNames().forEach(key -> messages.put(key, properties.getProperty(key)));
            return new MessageCatalog(moduleOf(resource), localeOf(resource), messages);
        } catch (IOException | IllegalArgumentException e) {
            throw new UnreadableMessageCatalogException(
                    "Cannot read message catalog %s; it must be a UTF-8 properties file".formatted(location), e);
        }
    }

    private static String moduleOf(Resource resource) throws IOException {
        var segments = resource.getURI().toString().split("/");
        return segments[segments.length - 2];
    }

    private static Locale localeOf(Resource resource) {
        var fileName = String.valueOf(resource.getFilename());
        var tag = fileName.substring(FILE_PREFIX.length(), fileName.length() - FILE_SUFFIX.length());
        return Locale.forLanguageTag(tag.replace('_', '-'));
    }
}
