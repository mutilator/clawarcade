using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace InternetClawMachine
{
    internal class Notifier
    {
        public static string MailServer = null;
        public static string MailFrom = null;

        public static async void SendEmail(string mailto, string subject, string message) => await Task.Run(delegate
                                                                                     {
                                                                                         try
                                                                                         {
                                                                                             if (MailServer == null || MailFrom == null)
                                                                                                 throw new Exception("Invalid mail server or from address.");

                                                                                             var emailObject = new System.Net.Mail.MailMessage();
                                                                                             emailObject.To.Add(mailto);
                                                                                             emailObject.Subject = subject;
                                                                                             emailObject.From = new System.Net.Mail.MailAddress(MailFrom);
                                                                                             emailObject.Body = message;
                                                                                             var smtp = new System.Net.Mail.SmtpClient(MailServer);
                                                                                             smtp.Send(emailObject);
                                                                                         }
                                                                                         catch (Exception ex)
                                                                                         {
                                                                                             var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                                                                                             Logger.WriteLog(Logger.ErrorLog, error);
                                                                                         }
                                                                                     });
        public static void SendDiscordMessage(string webHook, string message)
        {
            Task.Run(async delegate
            {

                try
                {
                    //send to discord
                    var client = new HttpClient();
                    var url = new JObject();
                    url.Add("content", message);

                    var data = new StringContent(JsonConvert.SerializeObject(url), Encoding.UTF8, "application/json");
                    var res = await client.PostAsync(webHook, data);
                }
                catch (Exception ex)
                {
                    var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                    Logger.WriteLog(Logger.ErrorLog, error);
                }

            });
        }
        
    }
}