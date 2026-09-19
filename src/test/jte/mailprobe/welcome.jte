@import com.frappe.platform.mail.MailTexts
@param MailTexts texts
@param String name
@param String code
<p>${texts.get("mailprobe.mail.welcome.greeting", name)}</p>
<p>${texts.get("mailprobe.mail.welcome.code", code)}</p>
