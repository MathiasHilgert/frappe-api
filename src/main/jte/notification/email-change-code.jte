@import com.frappe.platform.mail.MailTexts
@param MailTexts texts
@param String code
@param long minutes
<p>${texts.get("notification.mail.email-change-code.intro")}</p>
<p style="font-size:28px;font-weight:bold;letter-spacing:6px">${code}</p>
<p>${texts.get("notification.mail.code.expires", minutes)}</p>
<p>${texts.get("notification.mail.email-change-code.not-you")}</p>
