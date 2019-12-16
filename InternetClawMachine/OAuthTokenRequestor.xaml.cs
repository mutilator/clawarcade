using System;
using System.Windows;

namespace InternetClawMachine
{
    /// <summary>
    /// Interaction logic for OAuthTokenRequestor.xaml
    /// </summary>
    public partial class OAuthTokenRequestor : Window
    {
        public OAuthTokenRequestor()
        {
            InitializeComponent();
            this.Loaded += (object sender, RoutedEventArgs e) =>
            {
                //Add the message hook in the code behind since I got a weird bug when trying to do it in the XAML
                webBrowser.MessageHook += webBrowser_MessageHook;

                //Delete the cookies since the last authentication
                RemoveCookies();

                //Create the destination URL
                var destinationUrl = string.Format("https://api.twitch.tv/kraken/oauth2/authorize?client_id={0}&scope={1}&redirect_uri=http://localhost&response_type=token",
                   ClientId, //client_id
                   "chat_login%20channel_read%20channel_feed_read%20channel_check_subscription%20user_blocks_edit%20user_blocks_read%20user_read%20user_subscriptions" //scope
                );
                webBrowser.Navigate(destinationUrl);
            };
        }

        public string ClientId { get; set; }

        //The access token retrieved from facebook's authentication
        public string AccessToken { get; set; }

        private void webBrowser_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            //If the URL has an access_token, grab it and walk away...
            var url = e.Uri.Fragment;
            if (url.Contains("access_token") && url.Contains("#"))
            {
                url = (new System.Text.RegularExpressions.Regex("#")).Replace(url, "?", 1);
                AccessToken = System.Web.HttpUtility.ParseQueryString(url).Get("access_token");
                DialogResult = true;
                this.Close();
            }
        }

        private void RemoveCookies()
        {
            //Set the current user cookie to have expired yesterday
            var cookie = string.Format("api_token=; name=; unique_id=; login=; expires={0:R}; path=/;", DateTime.UtcNow.AddDays(-2).ToString("R"));
            Application.SetCookie(new Uri("https://api.twitch.tv"), cookie);

            var blah = Application.GetCookie(new Uri("https://api.twitch.tv"));
        }

        private void webBrowser_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
        {
            if (e.Uri.LocalPath == "/r.php")
            {
                MessageBox.Show("blah", "Could Not Create Account", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Cancel = true;
            }
        }

        private IntPtr webBrowser_MessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            //msg = 130 is the last call for when the window gets closed on a window.close() in javascript
            if (msg == 130)
            {
                this.Close();
            }
            return IntPtr.Zero;
        }
    }
}