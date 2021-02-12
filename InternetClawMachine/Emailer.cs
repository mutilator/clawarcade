using System;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InternetClawMachine
{
    internal class Notifier
    {
        public static string _mailServer = null;
        public static string _mailFrom = null;

        public static async void SendEmail(string mailto, string subject, string message) => await Task.Run(delegate
                                                                                     {
                                                                                         try
                                                                                         {
                                                                                             if (_mailServer == null || _mailFrom == null)
                                                                                                 throw new Exception("Invalid mail server or from address.");

                                                                                             var emailObject = new MailMessage();
                                                                                             emailObject.To.Add(mailto);
                                                                                             emailObject.Subject = subject;
                                                                                             emailObject.From = new MailAddress(_mailFrom);
                                                                                             emailObject.Body = message;
                                                                                             var smtp = new SmtpClient(_mailServer);
                                                                                             smtp.Send(emailObject);
                                                                                         }
                                                                                         catch (Exception ex)
                                                                                         {
                                                                                             var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                                                                                             Logger.WriteLog(Logger._errorLog, error);
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
                    Logger.WriteLog(Logger._errorLog, error);
                }

            });
        }
        
    }
}