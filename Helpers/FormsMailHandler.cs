using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Net.Mail;
using System.Net;
using System.Threading;

namespace Morpara.Helpers
{
    public static class FormsMailHandler
    {
        public static async Task SendForm<T>(this T formType, string json, ControllerBase controllerContext, MailFormModel MailModel, string viewPath, string subject = null) where T : Type
        {
            try
            {
                if (string.IsNullOrEmpty(MailModel.Sendtoadress))
                {
                    Log.Logger.Warning("Email alıcıları boş olamaz");
                    throw new NullReferenceException("Email alıcıları boş olamaz.");
                }

                string smtpHost = MailModel.SmtpHost;
                int smtpPort = MailModel.SmtpPort > 0 ? MailModel.SmtpPort : 587;
                string smtpUsername = MailModel.SmtpUsername;
                string smtpPassword = MailModel.SmtpPassword;
                bool enableSsl = MailModel.SSL;

                string fromEmail = MailModel.FromEmail;
                string notificationEmail = MailModel.NotificationEmail;
                string jiraEmail = MailModel.JiraEmail;
                string bccEmail = MailModel.BCCEmail;

                MailMessage message = new MailMessage(MailModel.FromEmail, !string.IsNullOrEmpty(MailModel.Sendtoadress) ? MailModel.Sendtoadress : notificationEmail)
                {
                    Body = RenderRazorViewToString(controllerContext, viewPath, JObject.Parse(json)),
                    IsBodyHtml = true,
                    Subject = !string.IsNullOrWhiteSpace(subject) ? subject : $"{formType.Name} - Gönderen {fromEmail}"
                };

                if (!string.IsNullOrEmpty(jiraEmail))
                {
                    message.CC.Add(jiraEmail);
                }

                if (!string.IsNullOrEmpty(bccEmail))
                {
                    message.Bcc.Add(bccEmail);
                }

                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

                using SmtpClient smtp = new SmtpClient(smtpHost, smtpPort)
                {
                    EnableSsl = enableSsl,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(smtpUsername, smtpPassword),
                    Timeout = 10000 // 10 saniye timeout ekle
                };

                // Asenkron olarak mail gönder
                await smtp.SendMailAsync(message).ConfigureAwait(false);
                Log.Logger.Information("E-posta başarıyla gönderildi.");
            }
            catch (Exception e)
            {
                Log.Logger.Error(e, "E-posta gönderilirken hata oluştu");
                throw; // Re-throw to allow handling in controller
            }
        }

        public static string RenderRazorViewToString(ControllerBase controller, string viewName, object model = null)
        {
            // Check if HttpContext is available (it won't be in Hangfire background jobs)
            if (controller?.HttpContext == null)
            {
                Log.Logger.Warning($"⚠️ HttpContext is null, cannot render view '{viewName}'. Returning simple HTML.");
                Log.Logger.Warning($"💡 TIP: Email template'i HTTP request sırasında render edin ve Hangfire job'a hazır HTML geçirin!");
                
                // Fallback: Create simple HTML from model
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("<!DOCTYPE html>");
                sb.AppendLine("<html><head><meta charset='utf-8'/><title>Form Submission</title></head><body>");
                sb.AppendLine("<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;'>");
                sb.AppendLine("<h2 style='color: #333; border-bottom: 2px solid #007bff; padding-bottom: 10px;'>Form Gönderimi</h2>");
                sb.AppendLine("<table border='1' cellpadding='8' cellspacing='0' style='width: 100%; border-collapse: collapse; margin-top: 20px;'>");
                
                if (model != null)
                {
                    var json = model as Newtonsoft.Json.Linq.JObject;
                    if (json != null)
                    {
                        foreach (var prop in json.Properties())
                        {
                            sb.AppendLine($"<tr><td style='background-color: #f8f9fa; font-weight: bold; width: 40%;'>{prop.Name}</td><td style='padding: 8px;'>{prop.Value}</td></tr>");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"<tr><td colspan='2' style='padding: 8px;'>{model}</td></tr>");
                    }
                }
                
                sb.AppendLine("</table>");
                sb.AppendLine($"<p style='color: #6c757d; font-size: 12px; margin-top: 20px; border-top: 1px solid #dee2e6; padding-top: 10px;'>Gönderim Zamanı: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
                sb.AppendLine("</div>");
                sb.AppendLine("</body></html>");
                
                return sb.ToString();
            }
            
            var viewEngine = controller.HttpContext.RequestServices.GetService(typeof(ICompositeViewEngine)) as ICompositeViewEngine;
            
            if (viewEngine == null)
            {
                Log.Logger.Error("ViewEngine is null");
                throw new InvalidOperationException("ViewEngine not found");
            }
            
            var viewResult = viewEngine.GetView("", viewName, false);

            if (!viewResult.Success)
            {
                Log.Logger.Error($"View '{viewName}' bulunamadı");
                throw new InvalidOperationException($"Unable to find view '{viewName}'");
            }

            var viewDictionary = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = model
            };

            using (var sw = new StringWriter())
            {
                var tempDataProvider = controller.HttpContext.RequestServices.GetService(typeof(ITempDataProvider)) as ITempDataProvider;
                
                var viewContext = new ViewContext(
                    controller.ControllerContext,
                    viewResult.View,
                    viewDictionary,
                    new TempDataDictionary(controller.HttpContext, tempDataProvider),
                    sw,
                    new HtmlHelperOptions()
                );

                var t = viewResult.View.RenderAsync(viewContext);
                t.Wait();

                return sw.GetStringBuilder().ToString();
            }
        }
    }

}
public class MailFormModel
{
    public string SmtpHost { get; set; }
    public int SmtpPort { get; set; }
    public string SmtpUsername { get; set; }
    public string SmtpPassword { get; set; }
    public bool SSL { get; set; }
    public string FromEmail { get; set; }
    public string NotificationEmail { get; set; }
    public string JiraEmail { get; set; }
    public string BCCEmail { get; set; }
    public string Sendtoadress { get; set; }
}
