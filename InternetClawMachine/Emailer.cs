using System;
using System.Threading.Tasks;

namespace InternetClawMachine
{
    internal class Emailer
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
    }
}