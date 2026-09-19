package com.frappe.platform.infrastructure.i18n;

import com.frappe.platform.i18n.Messages;
import java.util.Locale;
import org.springframework.context.MessageSource;
import org.springframework.context.NoSuchMessageException;
import org.springframework.context.support.MessageSourceAccessor;

/**
 * {@link Messages} over Spring's {@link MessageSourceAccessor}, which reads the current locale from
 * {@code LocaleContextHolder} (set per request by the {@code DispatcherServlet} from the locale chain).
 */
final class MessageSourceMessages implements Messages {

    private final MessageSourceAccessor accessor;

    /**
     * Creates the adapter.
     *
     * @param messageSource the application message source
     */
    MessageSourceMessages(MessageSource messageSource) {
        this.accessor = new MessageSourceAccessor(messageSource);
    }

    @Override
    public String get(String key, Object... args) {
        try {
            return accessor.getMessage(key, args);
        } catch (NoSuchMessageException e) {
            throw new UnknownMessageKeyException(key, e);
        }
    }

    @Override
    public String get(Locale locale, String key, Object... args) {
        try {
            return accessor.getMessage(key, args, locale);
        } catch (NoSuchMessageException e) {
            throw new UnknownMessageKeyException(key, e);
        }
    }
}
