using AVFoundation;
using AVKit;
using CoreFoundation;
using Foundation;
using Microsoft.Maui.Handlers;
using UIKit;

namespace Alcifra.Messenger.Views;

/// <summary>
/// The system player on iOS, reading the bytes from memory (#27): an asset at an address of a scheme of our own, which the
/// system cannot fetch, so it asks <see cref="MemoryAssetLoader"/> for every byte instead. Nothing is written to storage.
/// </summary>
internal sealed class MemoryPlayerHandler() : ViewHandler<MemoryPlayerSurface, UIView>(ViewMapper, ViewCommandMapper)
{
    // Any address of a scheme the system does not know sends every request to the loader; the name says what it is.
    private const string Address = "simplarchive-memory://player/content";

    private AVPlayerViewController? _controller;
    private AVPlayer? _player;
    private MemoryAssetLoader? _loader;

    protected override UIView CreatePlatformView() => new PlayerContainer();

    protected override void ConnectHandler(UIView platformView)
    {
        base.ConnectHandler(platformView);
        _loader = new MemoryAssetLoader(VirtualView.Bytes, VirtualView.Identifier);
        var asset = new AVUrlAsset(new NSUrl(Address));
        asset.ResourceLoader.SetDelegate(_loader, new DispatchQueue("Alcifra.Messenger.MemoryPlayer"));
        _player = new AVPlayer(new AVPlayerItem(asset));
        _controller = new AVPlayerViewController { Player = _player };

        // The controls show only for a player controller with a parent: the page's own, else the one in front.
        var parent = ParentController();
        parent?.AddChildViewController(_controller);
        ((PlayerContainer)platformView).Show(_controller.View!);
        _controller.DidMoveToParentViewController(parent);

        // Sound plays with the ring switch on silent, as a played video does in the system's own apps.
        var session = AVAudioSession.SharedInstance();
        session.SetCategory(AVAudioSessionCategory.Playback);
        session.SetActive(true);
        _player.Play();
    }

    protected override void DisconnectHandler(UIView platformView)
    {
        _player?.Pause();
        _player?.ReplaceCurrentItemWithPlayerItem(null);
        if (_controller is not null)
        {
            _controller.WillMoveToParentViewController(null);
            _controller.View?.RemoveFromSuperview();
            _controller.RemoveFromParentViewController();
            _controller.Player = null;
            _controller.Dispose();
        }

        _player?.Dispose();
        _loader?.Dispose();
        (_controller, _player, _loader) = (null, null, null);
        AVAudioSession.SharedInstance().SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation);
        base.DisconnectHandler(platformView);
    }

    private UIViewController? ParentController()
    {
        Element? element = VirtualView;
        while (element is not null and not Page)
        {
            element = element.Parent;
        }

        return element is Page { Handler: PageHandler { ViewController: { } own } } ? own : Platform.GetCurrentUIViewController();
    }

    // Lays the player's view over the whole of the space MAUI gives it.
    private sealed class PlayerContainer : UIView
    {
        private UIView? _shown;

        public void Show(UIView view)
        {
            _shown = view;
            AddSubview(view);
            SetNeedsLayout();
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            if (_shown is not null)
            {
                _shown.Frame = Bounds;
            }
        }
    }
}
