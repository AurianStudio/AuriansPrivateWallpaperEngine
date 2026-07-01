using Lively.Gallery.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Lively.UI.WinUI.Views.Pages.Gallery
{
    public sealed partial class GalleryView : Page
    {
        private bool isAuthenticated = false;
        private readonly List<(Type Page, string tag)> pages = new List<(Type Page, string tag)>
        {
            (typeof(GalleryLoginView), "login"),
            (typeof(GalleryLibraryView), "library"),
            (typeof(GalleryFeaturedView), "featured"),
            (typeof(GallerySubscriptionView), "subscription"),
            (typeof(ManageAccountView), "profile"),
            (typeof(GalleryBrowseView), "browse"),
        };

        public GalleryView()
        {
            this.InitializeComponent();
            var gallery = App.Services.GetRequiredService<GalleryClient>();

            if (gallery.UseVSthemes)
            {
                isAuthenticated = true;
                NavigatePage("library");
                return;
            }

            if (gallery.IsLoggedIn)
            {
                NavigatePage("library");
                isAuthenticated = true;
            }
            else
            {
                NavigatePage("login");
                navView.Margin = new Thickness(-50, 0, 0, 0);
                gallery.LoggedIn += (_, _) =>
                {
                    isAuthenticated = true;
                    this.DispatcherQueue.TryEnqueue(async () =>
                    {
                        await Task.Delay(2000);
                        NavigatePage("library");
                        navView.Margin = new Thickness(0);
                    });
                };
            }
        }

        private void navView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer != null && isAuthenticated)
            {
                var navItemTag = args.InvokedItemContainer.Tag.ToString();
                NavigatePage(navItemTag);
            }
        }

        private void NavigatePage(string tag, object arg = null)
        {
            var nextNavPageType = pages.FirstOrDefault(p => p.tag.Equals(tag)).Page;
            var preNavPageType = contentFrame.CurrentSourcePageType;
            if (!(nextNavPageType is null) && !Type.Equals(preNavPageType, nextNavPageType))
            {
                contentFrame.Navigate(nextNavPageType, arg, new DrillInNavigationTransitionInfo());

                var item = navView.MenuItems.FirstOrDefault(x => ((NavigationViewItem)x).Tag.ToString() == tag) ??
                    navView.FooterMenuItems.FirstOrDefault(x => ((NavigationViewItem)x).Tag.ToString() == tag);
                navView.SelectedItem = ((UIElement)item).Visibility != Visibility.Collapsed ? item : navView.SelectedItem;
            }
        }
    }
}
