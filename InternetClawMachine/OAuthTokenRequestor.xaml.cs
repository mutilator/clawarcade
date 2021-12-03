using System;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows;
using CefSharp;

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
            Browser.FrameLoadEnd += Browser_FrameLoadEnd;
            Browser.IsBrowserInitializedChanged += Browser_IsBrowserInitializedChanged;

        }
        private void Browser_IsBrowserInitializedChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (Browser.IsBrowserInitialized)
            {
                //Create the destination URL
                var destinationUrl = string.Format("https://api.twitch.tv/kraken/oauth2/authorize?client_id={0}&scope={1}&redirect_uri=http://localhost&response_type=token",
                   ClientId, //client_id
                   "chat_login%20channel_read%20channel_feed_read%20channel_check_subscription%20user_blocks_edit%20user_blocks_read%20user_read%20user_subscriptions%20clips:edit" //scope
                    );
                Browser.Load(destinationUrl);
            }
        }

        private void Browser_FrameLoadEnd(object sender, FrameLoadEndEventArgs e)
        {
            //If the URL has an access_token, grab it and walk away...
            var url = e.Url;
            if (url.Contains("access_token") && url.Contains("#"))
            {
                url = new Regex("#").Replace(url, "?", 1);
                url = url.Substring(url.IndexOf("?"));
                var parsed = HttpUtility.ParseQueryString(url);
                AccessToken = parsed.Get("access_token");
                Dispatcher?.BeginInvoke(new Action(() =>
                {
                    DialogResult = true;
                    Close();
                }));
            }
        }


        public string ClientId { get; set; }

        //The access token retrieved from facebook's authentication
        public string AccessToken { get; set; }

    }
}