using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuMappingHelper.Models;
using OsuMappingHelper.Services;
using osuTK;
using osuTK.Graphics;

namespace OsuMappingHelper.Components;

/// <summary>
/// A dialog for displaying update information and progress.
/// </summary>
public partial class UpdateDialog : CompositeDrawable
{
    private Container _dialogContainer = null!;
    private SpriteText _titleText = null!;
    private SpriteText _versionText = null!;
    private TextFlowContainer _descriptionText = null!;
    private SpriteText _statusText = null!;
    private Container _progressBarContainer = null!;
    private Box _progressBar = null!;
    private FillFlowContainer _buttonContainer = null!;
    private UpdateDialogButton _updateButton = null!;
    private UpdateDialogButton _cancelButton = null!;
    private UpdateDialogButton _laterButton = null!;

    private UpdateInfo? _updateInfo;
    private SquirrelUpdaterService? _updaterService;
    private bool _isDownloading;

    /// <summary>
    /// Event raised when the dialog is closed.
    /// </summary>
    public event Action? Closed;

    /// <summary>
    /// Event raised when the user requests to apply the update and restart.
    /// </summary>
    public event Action? UpdateRequested;

    public UpdateDialog()
    {
        RelativeSizeAxes = Axes.Both;
        Alpha = 0;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            // Dim background
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(0, 0, 0, 200)
            },
            // Dialog container
            _dialogContainer = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(400, 320),
                Masking = true,
                CornerRadius = 10,
                Children = new Drawable[]
                {
                    // Background
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(30, 30, 35, 255)
                    },
                    // Content
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(20),
                        Spacing = new Vector2(0, 12),
                        Children = new Drawable[]
                        {
                            // Title
                            _titleText = new SpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "Update Available",
                                Font = new FontUsage("", 25, "Bold"),
                                Colour = new Color4(255, 102, 170, 255)
                            },
                            // Version info
                            _versionText = new SpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "New version available",
                                Font = new FontUsage("", 19),
                                Colour = Color4.White
                            },
                            // Description container
                            new Container
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Size = new Vector2(360, 100),
                                Masking = true,
                                CornerRadius = 5,
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = new Color4(20, 20, 25, 255)
                                    },
                                    new BasicScrollContainer
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Padding = new MarginPadding(10),
                                        Child = _descriptionText = new TextFlowContainer
                                        {
                                            AutoSizeAxes = Axes.Y,
                                            RelativeSizeAxes = Axes.X,
                                            TextAnchor = Anchor.TopLeft
                                        }
                                    }
                                }
                            },
                            // Status text
                            _statusText = new SpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "",
                                Font = new FontUsage("", 17),
                                Colour = new Color4(180, 180, 180, 255)
                            },
                            // Progress bar container
                            _progressBarContainer = new Container
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Size = new Vector2(360, 8),
                                Masking = true,
                                CornerRadius = 4,
                                Alpha = 0,
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = new Color4(50, 50, 55, 255)
                                    },
                                    _progressBar = new Box
                                    {
                                        RelativeSizeAxes = Axes.Y,
                                        Width = 0,
                                        Colour = new Color4(255, 102, 170, 255)
                                    }
                                }
                            },
                            // Button container
                            _buttonContainer = new FillFlowContainer
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(10, 0),
                                Margin = new MarginPadding { Top = 10 },
                                Children = new Drawable[]
                                {
                                    _laterButton = new UpdateDialogButton("Later")
                                    {
                                        Size = new Vector2(100, 35),
                                        BackgroundColour = new Color4(80, 80, 85, 255)
                                    },
                                    _cancelButton = new UpdateDialogButton("Cancel")
                                    {
                                        Size = new Vector2(100, 35),
                                        BackgroundColour = new Color4(150, 60, 60, 255),
                                        Alpha = 0
                                    },
                                    _updateButton = new UpdateDialogButton("Update Now")
                                    {
                                        Size = new Vector2(120, 35),
                                        BackgroundColour = new Color4(255, 102, 170, 255)
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        _laterButton.Clicked += OnLaterClicked;
        _cancelButton.Clicked += OnCancelClicked;
        _updateButton.Clicked += OnUpdateClicked;
    }

    /// <summary>
    /// Shows the update dialog with the specified update information.
    /// </summary>
    public void Show(UpdateInfo updateInfo, SquirrelUpdaterService updaterService)
    {
        _updateInfo = updateInfo;
        _updaterService = updaterService;
        _isDownloading = false;

        // Update UI
        _versionText.Text = $"Version {updateInfo.TagName} is available (Current: {updaterService.CurrentVersion})";
        
        _descriptionText.Clear();
        if (!string.IsNullOrWhiteSpace(updateInfo.Body))
        {
            _descriptionText.AddText(updateInfo.Body, t =>
            {
                t.Font = new FontUsage("", 19);
                t.Colour = new Color4(200, 200, 200, 255);
            });
        }
        else
        {
            _descriptionText.AddText("No release notes available.", t =>
            {
                t.Font = new FontUsage("", 19);
                t.Colour = new Color4(150, 150, 150, 255);
            });
        }

        _statusText.Text = $"Download size: {FormatBytes(updateInfo.DownloadSize)}";
        
        // Reset UI state
        _progressBarContainer.Alpha = 0;
        _progressBar.Width = 0;
        _laterButton.Alpha = 1;
        _cancelButton.Alpha = 0;
        _updateButton.Alpha = 1;
        _updateButton.Enabled = true;
        _updateButton.Text = "Update Now";

        // Subscribe to progress events
        if (_updaterService != null)
        {
            _updaterService.DownloadProgressChanged += OnDownloadProgress;
        }

        // Show dialog with animation
        this.FadeIn(200, Easing.OutQuint);
        _dialogContainer.ScaleTo(0.9f).ScaleTo(1f, 200, Easing.OutQuint);
    }

    /// <summary>
    /// Hides the update dialog.
    /// </summary>
    public new void Hide()
    {
        if (_updaterService != null)
        {
            _updaterService.DownloadProgressChanged -= OnDownloadProgress;
        }

        this.FadeOut(200, Easing.OutQuint);
        Closed?.Invoke();
    }

    private void OnLaterClicked()
    {
        if (!_isDownloading)
        {
            Hide();
        }
    }

    private void OnCancelClicked()
    {
        if (_isDownloading)
        {
            _updaterService?.CancelDownload();
            _isDownloading = false;
            
            // Reset UI
            _statusText.Text = "Download cancelled";
            _progressBarContainer.FadeOut(200);
            _laterButton.FadeIn(200);
            _cancelButton.FadeOut(200);
            _updateButton.FadeIn(200);
            _updateButton.Enabled = true;
            _updateButton.Text = "Update Now";
        }
    }

    private async void OnUpdateClicked()
    {
        if (_updateInfo == null || _updaterService == null)
            return;

        if (_updateButton.Text == "Restart Now")
        {
            // User confirmed restart
            UpdateRequested?.Invoke();
            _updaterService.StartUpdateAndRestart(_updateInfo);
            return;
        }

        _isDownloading = true;

        // Update UI for download state
        _laterButton.FadeOut(200);
        _cancelButton.FadeIn(200);
        _updateButton.Enabled = false;
        _progressBarContainer.FadeIn(200);
        _statusText.Text = "Starting download...";

        var progress = new Progress<DownloadProgressEventArgs>(args =>
        {
            Schedule(() =>
            {
                _progressBar.ResizeWidthTo(args.ProgressPercentage * 3.6f, 100);
                _statusText.Text = args.Status;
            });
        });

        var success = await _updaterService.DownloadAndApplyUpdateAsync(_updateInfo, progress);

        Schedule(() =>
        {
            _isDownloading = false;

            if (success)
            {
                _statusText.Text = "Update ready! Restart to apply.";
                _progressBar.ResizeWidthTo(360, 100);
                _cancelButton.FadeOut(200);
                _updateButton.FadeIn(200);
                _updateButton.Enabled = true;
                _updateButton.Text = "Restart Now";
            }
            else
            {
                _statusText.Text = "Download failed. Please try again.";
                _progressBarContainer.FadeOut(200);
                _laterButton.FadeIn(200);
                _cancelButton.FadeOut(200);
                _updateButton.FadeIn(200);
                _updateButton.Enabled = true;
                _updateButton.Text = "Retry";
            }
        });
    }

    private void OnDownloadProgress(object? sender, DownloadProgressEventArgs e)
    {
        Schedule(() =>
        {
            _progressBar.ResizeWidthTo(e.ProgressPercentage * 3.6f, 50);
            _statusText.Text = e.Status;
        });
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    protected override bool OnClick(ClickEvent e)
    {
        // Prevent clicks from passing through
        return true;
    }
}

/// <summary>
/// A styled button for the update dialog.
/// </summary>
public partial class UpdateDialogButton : CompositeDrawable
{
    private Box _background = null!;
    private Box _hoverOverlay = null!;
    private SpriteText _textSprite = null!;
    private bool _isEnabled = true;
    private string _text;

    public Color4 BackgroundColour { get; set; } = new Color4(255, 102, 170, 255);

    public event Action? Clicked;

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            if (_textSprite != null)
                _textSprite.Text = value;
        }
    }

    public bool Enabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (_background != null)
            {
                _background.FadeColour(_isEnabled ? BackgroundColour : new Color4(80, 80, 85, 255), 100);
            }
        }
    }

    public UpdateDialogButton(string text)
    {
        _text = text;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 5;

        InternalChildren = new Drawable[]
        {
            _background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = BackgroundColour
            },
            _hoverOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            _textSprite = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = _text,
                Font = new FontUsage("", 17, "Bold"),
                Colour = Color4.White
            }
        };
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (_isEnabled)
        {
            _hoverOverlay.FadeTo(0.15f, 100);
        }
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _hoverOverlay.FadeTo(0, 100);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        if (_isEnabled)
        {
            Clicked?.Invoke();
            _hoverOverlay.FadeTo(0.3f, 50).Then().FadeTo(0.15f, 100);
        }
        return true;
    }
}
