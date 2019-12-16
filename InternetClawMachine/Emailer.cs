using System;
using System.Threading.Tasks;

namespace InternetClawMachine
{
    internal class Emailer
    {
        private static string _mailServer = "10.1.2.1";
        private static string _mailFrom = "sdgusler@gmail.com";

        public static async void SendEmail(string mailto, string subject, string message) => await Task.Run(delegate
                                                                                     {
                                                                                         try
                                                                                         {
                                                                                             var emailObject = new System.Net.Mail.MailMessage();
                                                                                             emailObject.To.Add(mailto);
                                                                                             emailObject.Subject = subject;
                                                                                             emailObject.From = new System.Net.Mail.MailAddress(_mailFrom);
                                                                                             emailObject.Body = message;
                                                                                             var smtp = new System.Net.Mail.SmtpClient(_mailServer);
                                                                                             smtp.Send(emailObject);
                                                                                         }
                                                                                         catch (Exception ex)
                                                                                         {
                                                                                             var error = string.Format("ERROR {0} {1}", ex.Message, ex);
                                                                                             Logger.WriteLog(Logger.ErrorLog, error);
                                                                                         }
                                                                                     });
    }
}