using System.Configuration;
using System.Net;
using System.Net.Mail;

namespace ASP.app_code.code
{
    public class Email
    {
        public static void Send(MailMessage mail)
        {
            try
            {
                using (SmtpClient client = CreateMailClient())
                {
                    client.Send(mail);
                    mail.Dispose();
                }
            }
            catch
            { }
        }

        private static SmtpClient CreateMailClient()
        {
            string host = ConfigurationManager.AppSettings["mail:host"];
            string username = ConfigurationManager.AppSettings["mail:username"];
            string password = ConfigurationManager.AppSettings["mail:password"];

            int port;
            if (!int.TryParse(ConfigurationManager.AppSettings["mail:port"], out port))
                port = 587;

            bool enableSSL;
            if (!bool.TryParse(ConfigurationManager.AppSettings["mail:enableSSL"], out enableSSL))
                enableSSL = true;

            var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSSL,
                Credentials = new NetworkCredential(username, password)
            };

            return client;
        }
    }
}