using System.Globalization;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using OsuMappingHelper.Models;
using OsuMappingHelper.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TextBox = osu.Framework.Graphics.UserInterface.TextBox;
using Image = SixLabors.ImageSharp.Image;

namespace OsuMappingHelper.Components;

/// <summary>
/// Panel for creating marathon beatmaps by combining multiple maps.
/// </summary>
public partial class MarathonCreatorPanel : CompositeDrawable
{
    private FillFlowContainer _listContainer = null!;
    private ModernButton _addButton = null!;
    private ModernButton _addPauseButton = null!;
    private StyledTextBox _pauseDurationTextBox = null!;
    private ModernButton _createButton = null!;
    private ModernButton _clearButton = null!;
    private ModernButton _msdButton = null!;
    private StyledTextBox _titleTextBox = null!;
    private StyledTextBox _artistTextBox = null!;
    private StyledTextBox _creatorTextBox = null!;
    private StyledTextBox _versionTextBox = null!;
    private StyledTextBox _centerTextBox = null!;
    private SpriteText _summaryText = null!;
    private SpriteText _durationText = null!;
    private SpriteText _glitchValueText = null!;
    
    // Preview components
    private Container _previewContainer = null!;
    private Sprite _previewSprite = null!;
    private SpriteText _previewStatusText = null!;
    private ModernButton _refreshPreviewButton = null!;
    private CancellationTokenSource? _previewCancellation;
    private bool _previewNeedsUpdate = false;
    
    private readonly MarathonCreatorService _marathonService = new();
    
    [Resolved]
    private IRenderer Renderer { get; set; } = null!;
    
    private readonly BindableFloat _glitchIntensity = new BindableFloat(0f)
    {
        MinValue = 0f,
        MaxValue = 1f,
        Precision = 0.05f
    };

    private readonly List<MarathonEntry> _entries = new();
    private OsuFile? _currentBeatmap;

    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

    /// <summary>
    /// Event raised when marathon creation is requested.
    /// </summary>
    public event Action<List<MarathonEntry>, MarathonMetadata>? CreateMarathonRequested;

    /// <summary>
    /// Event raised when MSD recalculation is requested for all entries.
    /// </summary>
    public event Action<List<MarathonEntry>>? RecalculateMsdRequested;

    /// <summary>
    /// Event raised when loading starts.
    /// </summary>
    public event Action<string>? LoadingStarted;

    /// <summary>
    /// Event raised when loading status changes.
    /// </summary>
    public event Action<string>? LoadingStatusChanged;

    /// <summary>
    /// Event raised when loading finishes.
    /// </summary>
    public event Action? LoadingFinished;

    public MarathonCreatorPanel()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 12),
                Children = new Drawable[]
                {
                    // Maps List Section
                    CreateSection("Maps in Marathon", new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Children = new Drawable[]
                            {
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(8, 0),
                                    Children = new Drawable[]
                                    {
                                            _addButton = new ModernButton("Add Current Map")
                                            {
                                                Size = new Vector2(140, 32),
                                                Enabled = false
                                            },
                                            _addPauseButton = new ModernButton("Pause", enabledColor: new Color4(60, 180, 60, 255))
                                            {
                                                Size = new Vector2(70, 32),
                                            },
                                            _pauseDurationTextBox = new StyledTextBox
                                            {
                                                Size = new Vector2(40, 32),
                                                Text = "5",
                                                PlaceholderText = "sec"
                                            },
                                            _clearButton = new ModernButton("Clear All", enabledColor: new Color4(180, 60, 60, 255))
                                            {
                                                Size = new Vector2(90, 32),
                                                Enabled = false
                                            },
                                            _msdButton = new ModernButton("MSD")
                                            {
                                                Size = new Vector2(50, 32),
                                                Enabled = false
                                            }
                                    }
                                }
                            }
                        },
                        // List container with scroll
                        new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 200,
                            Masking = true,
                            CornerRadius = 6,
                            Margin = new MarginPadding { Top = 8 },
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = new Color4(30, 30, 35, 255)
                                },
                                new BasicScrollContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    ClampExtension = 10,
                                    ScrollbarVisible = true,
                                    Child = _listContainer = new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, 4),
                                        Padding = new MarginPadding(8)
                                    }
                                }
                            }
                        },
                        // Summary
                        new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Margin = new MarginPadding { Top = 4 },
                            Children = new Drawable[]
                            {
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(16, 0),
                                    Children = new Drawable[]
                                    {
                                        _summaryText = new SpriteText
                                        {
                                            Text = "0 maps",
                                            Font = new FontUsage("", 15),
                                            Colour = new Color4(140, 140, 140, 255)
                                        },
                                        _durationText = new SpriteText
                                        {
                                            Text = "Total: 0:00",
                                            Font = new FontUsage("", 15),
                                            Colour = new Color4(140, 140, 140, 255)
                                        }
                                    }
                                }
                            }
                        }
                    }),
                    // Metadata Section
                    CreateSection("Marathon Metadata", new Drawable[]
                    {
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 8),
                            Children = new Drawable[]
                            {
                                CreateLabeledInput("Title", out _titleTextBox, "Marathon"),
                                CreateLabeledInput("Artist", out _artistTextBox, "Various Artists"),
                                CreateLabeledInput("Creator", out _creatorTextBox, "Companella"),
                                CreateLabeledInput("Difficulty Name", out _versionTextBox, "Marathon"),
                                CreateLabeledInput("Center Symbol (max 3)", out _centerTextBox, ""),
                                CreateSymbolSelector(),
                                CreateGlitchSlider()
                            }
                        }
                    }),
                    // Background Preview Section
                    CreateSection("Background Preview", new Drawable[]
                    {
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 8),
                            Children = new Drawable[]
                            {
                                // Preview container with 16:9 aspect ratio
                                _previewContainer = new Container
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 135, // 240 * 9/16 = 135 (for width ~240)
                                    Masking = true,
                                    CornerRadius = 6,
                                    Children = new Drawable[]
                                    {
                                        new Box
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Colour = new Color4(20, 20, 25, 255)
                                        },
                                        _previewSprite = new Sprite
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            FillMode = FillMode.Fit,
                                            Anchor = Anchor.Centre,
                                            Origin = Anchor.Centre,
                                            Alpha = 0
                                        },
                                        _previewStatusText = new SpriteText
                                        {
                                            Text = "Add maps to see preview",
                                            Font = new FontUsage("", 14),
                                            Colour = new Color4(100, 100, 100, 255),
                                            Anchor = Anchor.Centre,
                                            Origin = Anchor.Centre
                                        }
                                    }
                                },
                                // Refresh button
                                _refreshPreviewButton = new ModernButton("Refresh Preview")
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 28,
                                    Enabled = false
                                }
                            }
                        }
                    }),
                    // Create Button
                    _createButton = new ModernButton("Create Marathon")
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 40,
                        Enabled = false
                    }
                }
            }
        };

        // Wire up events
        _addButton.Clicked += OnAddClicked;
        _addPauseButton.Clicked += OnAddPauseClicked;
        _clearButton.Clicked += OnClearClicked;
        _msdButton.Clicked += OnMsdClicked;
        _createButton.Clicked += OnCreateClicked;
        _refreshPreviewButton.Clicked += OnRefreshPreviewClicked;
        _centerTextBox.OnCommit += (_, _) => MarkPreviewNeedsUpdate();
    }

    private Container CreateSection(string title, Drawable[] content)
    {
        return new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children = new Drawable[]
            {
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 6),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = title,
                            Font = new FontUsage("", 15, "Bold"),
                            Colour = new Color4(180, 180, 180, 255)
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Children = content
                        }
                    }
                }
            }
        };
    }

    private Container CreateLabeledInput(string label, out StyledTextBox textBox, string defaultValue)
    {
        textBox = new StyledTextBox
        {
            RelativeSizeAxes = Axes.X,
            Height = 32,
            Text = defaultValue
        };

        return new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Child = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = label,
                        Font = new FontUsage("Roboto-Regular", 13),
                        Colour = new Color4(100, 100, 100, 255)
                    },
                    textBox
                }
            }
        };
    }

    // Symbol categories for the center text selector
    private static readonly string[] GreekUppercase = { 
        "\u0391", "\u0392", "\u0393", "\u0394", "\u0395", "\u0396", "\u0397", "\u0398", 
        "\u0399", "\u039A", "\u039B", "\u039C", "\u039D", "\u039E", "\u039F", "\u03A0", 
        "\u03A1", "\u03A3", "\u03A4", "\u03A5", "\u03A6", "\u03A7", "\u03A8", "\u03A9" 
    }; // A-O (Alpha to Omega)
    
    private static readonly string[] GreekLowercase = { 
        "\u03B1", "\u03B2", "\u03B3", "\u03B4", "\u03B5", "\u03B6", "\u03B7", "\u03B8", 
        "\u03B9", "\u03BA", "\u03BB", "\u03BC", "\u03BD", "\u03BE", "\u03BF", "\u03C0", 
        "\u03C1", "\u03C3", "\u03C4", "\u03C5", "\u03C6", "\u03C7", "\u03C8", "\u03C9" 
    }; // alpha to omega
    
    private static readonly string[] SpecialSymbols = { 
        "\u2190", "\u2191", "\u2192", "\u2193",   // Arrows: left, up, right, down
        "\u2194", "\u2195",                       // Arrows: left-right, up-down
        "\u221E", "\u2022", "\u2020", "\u2021"    // Misc: infinity, bullet, dagger, double dagger
    };

    private Container CreateSymbolSelector()
    {
        var symbolFlow = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Full,
            Spacing = new Vector2(2, 2)
        };

        // Add Greek uppercase
        foreach (var symbol in GreekUppercase)
        {
            symbolFlow.Add(CreateSymbolButton(symbol));
        }
        
        // Add Greek lowercase
        foreach (var symbol in GreekLowercase)
        {
            symbolFlow.Add(CreateSymbolButton(symbol));
        }
        
        // Add special symbols
        foreach (var symbol in SpecialSymbols)
        {
            symbolFlow.Add(CreateSymbolButton(symbol));
        }

        return new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Child = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = "Insert Symbol:",
                        Font = new FontUsage("", 13),
                        Colour = new Color4(100, 100, 100, 255)
                    },
                    symbolFlow
                }
            }
        };
    }

    private SymbolButton CreateSymbolButton(string symbol)
    {
        var btn = new SymbolButton(symbol)
        {
            Size = new Vector2(24, 24)
        };
        btn.Clicked += () => InsertSymbol(symbol);
        return btn;
    }

    private void InsertSymbol(string symbol)
    {
        // Only allow up to 3 characters
        if (_centerTextBox.Text.Length >= 3) return;
        _centerTextBox.Text += symbol;
    }

    private Container CreateGlitchSlider()
    {
        return new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Child = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = "Glitch Effects:",
                        Font = new FontUsage("", 13),
                        Colour = new Color4(100, 100, 100, 255)
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = "0%",
                                Font = new FontUsage("", 13),
                                Colour = new Color4(100, 100, 100, 255),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            },
                            new Container
                            {
                                Width = 200,
                                Height = 24,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Children = new Drawable[]
                                {
                                    // Track background
                                    new Container
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Height = 4,
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Masking = true,
                                        CornerRadius = 2,
                                        Child = new Box
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Colour = new Color4(60, 60, 65, 255)
                                        }
                                    },
                                    new GlitchSliderBar
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Height = 24,
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Current = _glitchIntensity
                                    }
                                }
                            },
                            new SpriteText
                            {
                                Text = "100%",
                                Font = new FontUsage("", 13),
                                Colour = new Color4(100, 100, 100, 255),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            },
                            _glitchValueText = new SpriteText
                            {
                                Text = "0%",
                                Font = new FontUsage("", 15, "Bold"),
                                Colour = _accentColor,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Margin = new MarginPadding { Left = 8 }
                            }
                        }
                    }
                }
            }
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        
        _glitchIntensity.BindValueChanged(e =>
        {
            _glitchValueText.Text = $"{e.NewValue:P0}";
            MarkPreviewNeedsUpdate();
        }, true);
    }

    private void OnRefreshPreviewClicked()
    {
        _ = GeneratePreviewAsync();
    }

    private void MarkPreviewNeedsUpdate()
    {
        _previewNeedsUpdate = true;
        _refreshPreviewButton.Enabled = _entries.Any(e => !e.IsPause);
    }

    private async Task GeneratePreviewAsync()
    {
        // Cancel any existing preview generation
        _previewCancellation?.Cancel();
        _previewCancellation = new CancellationTokenSource();
        var token = _previewCancellation.Token;

        var mapEntries = _entries.Where(e => !e.IsPause && e.OsuFile != null).ToList();
        if (mapEntries.Count == 0)
        {
            Schedule(() =>
            {
                _previewSprite.FadeTo(0, 150);
                _previewStatusText.Text = "Add maps to see preview";
                _previewStatusText.FadeTo(1, 150);
            });
            return;
        }

        Schedule(() =>
        {
            _previewStatusText.Text = "Generating preview...";
            _previewStatusText.FadeTo(1, 100);
        });

        try
        {
            var previewBytes = await Task.Run(async () =>
            {
                return await _marathonService.GenerateBackgroundPreviewAsync(
                    new List<MarathonEntry>(_entries),
                    _centerTextBox.Text,
                    _glitchIntensity.Value,
                    480, 270,
                    token
                );
            }, token);

            if (token.IsCancellationRequested) return;

            if (previewBytes == null || previewBytes.Length == 0)
            {
                Schedule(() =>
                {
                    _previewStatusText.Text = "Preview generation failed";
                });
                return;
            }

            // Load the image as a texture
            Schedule(() =>
            {
                try
                {
                    using var stream = new MemoryStream(previewBytes);
                    using var image = Image.Load<Rgba32>(stream);
                    
                    var texture = Renderer.CreateTexture(image.Width, image.Height);
                    texture.SetData(new TextureUpload(image.Clone()));

                    _previewSprite.Texture = texture;
                    _previewSprite.FadeTo(1, 200, Easing.OutQuint);
                    _previewStatusText.FadeTo(0, 100);
                    _previewNeedsUpdate = false;
                }
                catch
                {
                    _previewStatusText.Text = "Failed to display preview";
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Preview generation was cancelled
        }
        catch (Exception ex)
        {
            Schedule(() =>
            {
                _previewStatusText.Text = $"Error: {ex.Message}";
            });
        }
    }

    private partial class GlitchSliderBar : BasicSliderBar<float>
    {
        private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);

        [BackgroundDependencyLoader]
        private void load()
        {
            BackgroundColour = Color4.Transparent;
            SelectionColour = _accentColor;
        }
    }

    private partial class SymbolButton : CompositeDrawable
    {
        private readonly string _symbol;
        private Box _background = null!;
        public event Action? Clicked;

        private readonly Color4 _normalBg = new Color4(50, 50, 55, 255);
        private readonly Color4 _hoverBg = new Color4(70, 70, 80, 255);
        private readonly Color4 _clickBg = new Color4(90, 90, 100, 255);

        // Use test font for Unicode character support (Greek letters, symbols)
        private static readonly FontUsage UnicodeFont = new FontUsage("Noto-Basic", 17);

        public SymbolButton(string symbol)
        {
            _symbol = symbol;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Masking = true;
            CornerRadius = 4;

            InternalChildren = new Drawable[]
            {
                _background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = _normalBg
                },
                new SpriteText
                {
                    Text = _symbol,
                    Font = UnicodeFont,
                    Colour = Color4.White,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre
                }
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            _background.FadeColour(_hoverBg, 100);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            _background.FadeColour(_normalBg, 100);
            base.OnHoverLost(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            _background.FadeColour(_clickBg, 50).Then().FadeColour(_hoverBg, 100);
            Clicked?.Invoke();
            return true;
        }
    }

    /// <summary>
    /// Sets the current beatmap that can be added to the list.
    /// </summary>
    public void SetCurrentBeatmap(OsuFile? osuFile)
    {
        _currentBeatmap = osuFile;
        _addButton.Enabled = osuFile != null;
    }

    private void OnAddClicked()
    {
        if (_currentBeatmap == null) return;

        // Check if already in list (only for non-pause entries)
        if (_entries.Any(e => !e.IsPause && e.OsuFile?.FilePath == _currentBeatmap.FilePath))
        {
            return; // Already added
        }

        // Create entry
        var entry = MarathonEntry.FromOsuFile(_currentBeatmap);
        _entries.Add(entry);

        // Add to UI
        RefreshList();
        UpdateSummary();
        MarkPreviewNeedsUpdate();
        _ = GeneratePreviewAsync();
    }

    private void OnAddPauseClicked()
    {
        // Parse duration from text box
        if (!double.TryParse(_pauseDurationTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
        {
            duration = 5.0; // Default to 5 seconds
        }

        duration = Math.Clamp(duration, 0.1, 300.0); // Clamp between 0.1s and 5 minutes

        // Create pause entry
        var pauseEntry = MarathonEntry.CreatePause(duration);
        _entries.Add(pauseEntry);

        // Add to UI
        RefreshList();
        UpdateSummary();
        // No need to regenerate preview for pause entries
    }

    private void OnClearClicked()
    {
        _entries.Clear();
        RefreshList();
        UpdateSummary();
        MarkPreviewNeedsUpdate();
        _ = GeneratePreviewAsync();
    }

        private void OnMsdClicked()
        {
            if (_entries.Count == 0) return;
            RecalculateMsdRequested?.Invoke(new List<MarathonEntry>(_entries));
        }

        private void OnCreateClicked()
    {
        if (_entries.Count == 0) return;

        var metadata = new MarathonMetadata
        {
            Title = _titleTextBox.Text,
            Artist = _artistTextBox.Text,
            Creator = _creatorTextBox.Text,
            Version = _versionTextBox.Text,
            CenterText = _centerTextBox.Text,
            GlitchIntensity = _glitchIntensity.Value
        };

        CreateMarathonRequested?.Invoke(new List<MarathonEntry>(_entries), metadata);
    }

    public void RefreshList()
    {
        _listContainer.Clear();

        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var entryRow = new MarathonEntryRow(entry, i, _accentColor)
            {
                RelativeSizeAxes = Axes.X,
                Height = 52
            };
            entryRow.DeleteRequested += OnEntryDeleteRequested;
            entryRow.MoveUpRequested += OnEntryMoveUpRequested;
            entryRow.MoveDownRequested += OnEntryMoveDownRequested;
            entryRow.RateChanged += OnEntryRateChanged;
            _listContainer.Add(entryRow);
        }

            _clearButton.Enabled = _entries.Count > 0;
            _msdButton.Enabled = _entries.Count > 0;
            _createButton.Enabled = _entries.Count > 0;
    }

    private void OnEntryDeleteRequested(MarathonEntry entry)
    {
        bool wasMapEntry = !entry.IsPause;
        _entries.Remove(entry);
        RefreshList();
        UpdateSummary();
        if (wasMapEntry)
        {
            MarkPreviewNeedsUpdate();
            _ = GeneratePreviewAsync();
        }
    }

    private void OnEntryMoveUpRequested(MarathonEntry entry)
    {
        var index = _entries.IndexOf(entry);
        if (index > 0)
        {
            _entries.RemoveAt(index);
            _entries.Insert(index - 1, entry);
            RefreshList();
            if (!entry.IsPause)
            {
                MarkPreviewNeedsUpdate();
                _ = GeneratePreviewAsync();
            }
        }
    }

    private void OnEntryMoveDownRequested(MarathonEntry entry)
    {
        var index = _entries.IndexOf(entry);
        if (index < _entries.Count - 1)
        {
            _entries.RemoveAt(index);
            _entries.Insert(index + 1, entry);
            RefreshList();
            if (!entry.IsPause)
            {
                MarkPreviewNeedsUpdate();
                _ = GeneratePreviewAsync();
            }
        }
    }

    private void OnEntryRateChanged(MarathonEntry entry, double newRate)
    {
        entry.Rate = newRate;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        _summaryText.Text = $"{_entries.Count} map{(_entries.Count != 1 ? "s" : "")}";

        // Calculate total duration at rate
        double totalMs = _entries.Sum(e => e.EffectiveDurationAtRate);
        var totalTime = TimeSpan.FromMilliseconds(totalMs);
        _durationText.Text = $"Total: {(int)totalTime.TotalMinutes}:{totalTime.Seconds:D2}";
    }

    /// <summary>
    /// Sets the enabled state of the create button.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        _createButton.Enabled = enabled && _entries.Count > 0;
        _addButton.Enabled = enabled && _currentBeatmap != null;
    }
}

/// <summary>
/// A single row in the marathon entry list.
/// </summary>
public partial class MarathonEntryRow : CompositeDrawable
{
    private readonly MarathonEntry _entry;
    private readonly int _index;
    private readonly Color4 _accentColor;

    private Box _background = null!;
    private StyledTextBox _rateTextBox = null!;

    public event Action<MarathonEntry>? DeleteRequested;
    public event Action<MarathonEntry>? MoveUpRequested;
    public event Action<MarathonEntry>? MoveDownRequested;
    public event Action<MarathonEntry, double>? RateChanged;

    private readonly Color4 _normalBg = new Color4(40, 40, 45, 255);
    private readonly Color4 _hoverBg = new Color4(50, 50, 55, 255);
    private readonly Color4 _pauseBg = new Color4(80, 80, 100, 255);
    private readonly Color4 _pauseHoverBg = new Color4(90, 90, 115, 255);
    private readonly Color4 _deleteBg = new Color4(180, 60, 60, 255);
    private readonly Color4 _deleteHoverBg = new Color4(200, 80, 80, 255);

    // Skillset colors matching the MSD charts
    private static readonly Dictionary<string, Color4> SkillsetColors = new()
    {
        { "overall", new Color4(200, 200, 200, 255) },
        { "stream", new Color4(100, 180, 255, 255) },
        { "jumpstream", new Color4(100, 220, 100, 255) },
        { "handstream", new Color4(255, 180, 100, 255) },
        { "stamina", new Color4(180, 100, 255, 255) },
        { "jackspeed", new Color4(255, 100, 100, 255) },
        { "chordjack", new Color4(255, 220, 100, 255) },
        { "technical", new Color4(100, 220, 220, 255) },
        { "unknown", new Color4(150, 150, 150, 255) }
    };

    public MarathonEntryRow(MarathonEntry entry, int index, Color4 accentColor)
    {
        _entry = entry;
        _index = index;
        _accentColor = accentColor;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 4;

        var pauseColor = new Color4(80, 80, 100, 255);

        InternalChildren = new Drawable[]
        {
            _background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = _entry.IsPause ? pauseColor : _normalBg
            },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Padding = new MarginPadding { Left = 10, Right = 6, Top = 6, Bottom = 6 },
                Spacing = new Vector2(8, 0),
                Children = new Drawable[]
                {
                    // Index number
                    new Container
                    {
                        Size = new Vector2(24, 38),
                        Child = new SpriteText
                        {
                            Text = $"{_index + 1}.",
                            Font = new FontUsage("", 17, "Bold"),
                            Colour = _accentColor,
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre
                        }
                    },
                    // Map info section (title, version, creator, msd) or pause info
                    _entry.IsPause ? CreatePauseInfoSection() : CreateMapInfoSection(),
                }
            },
            // Action buttons on the right side
            CreateActionButtonsSection()
        };

        if (!_entry.IsPause)
        {
            _rateTextBox.OnCommit += OnRateCommit;
        }
    }

    private Drawable CreateMapInfoSection()
    {
        return new FillFlowContainer
        {
            AutoSizeAxes = Axes.X,
            RelativeSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Padding = new MarginPadding { Bottom = 60 },
            Spacing = new Vector2(0, 0),
            Children = new Drawable[]
            {
                new MarqueeText
                {
                    Text = _entry.Title,
                    Font = new FontUsage("", 15, "Bold"),
                    Colour = Color4.White,
                    Width = 200,
                    Height = 16
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.X,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(6, 0),
                    Children = new Drawable[]
                    {
                        new MarqueeText
                        {
                            Text = $"[{_entry.Version}]",
                            Font = new FontUsage("", 13),
                            Colour = new Color4(160, 160, 160, 255),
                            Width = 100,
                            Height = 16,
                        },
                        new MarqueeText
                        {
                            Text = $"by {_entry.Creator}",
                            Font = new FontUsage("", 13),
                            Colour = new Color4(120, 120, 120, 255),
                            Width = 75,
                            Height = 16,
                        }
                    }
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Padding = new MarginPadding {Top = 8},
                    Spacing = new Vector2(3, 0),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = $"{_entry.MsdValues?.Overall ?? 0.0f }",
                            Font = new FontUsage("", 13),
                            Colour = SkillsetColors.GetValueOrDefault("overall")
                        },
                        new SpriteText
                        {
                            Text = $"{_entry.MsdValues?.Stream ?? 0.0f }",
                            Font = new FontUsage("", 13),
                            Colour = SkillsetColors.GetValueOrDefault("stream")
                        },
                        new SpriteText
                        {
                            Text = $"{_entry.MsdValues?.Jumpstream ?? 0.0f }",
                            Font = new FontUsage("", 13),
                            Colour = SkillsetColors.GetValueOrDefault("jumpstream")
                        },
                        new SpriteText
                        {
                            Text = $"{_entry.MsdValues?.Handstream ?? 0.0f }",
                            Font = new FontUsage("", 13),
                            Colour = SkillsetColors.GetValueOrDefault("handstream")
                        },
                        new SpriteText
                        {
                            Text = $"{_entry.MsdValues?.Stamina ?? 0.0f }",
                            Font = new FontUsage("", 13),
                            Colour = SkillsetColors.GetValueOrDefault("stamina")
                        },
                        new SpriteText
                        {
                            Text = $"{_entry.MsdValues?.Jackspeed ?? 0.0f }",
                            Font = new FontUsage("", 13),
                            Colour = SkillsetColors.GetValueOrDefault("jackspeed")
                        },
                        new SpriteText
                        {
                            Text = $"{_entry.MsdValues?.Chordjack ?? 0.0f }",
                            Font = new FontUsage("", 13),
                            Colour = SkillsetColors.GetValueOrDefault("chordjack")
                        },
                        new SpriteText
                        {
                            Text = $"{_entry.MsdValues?.Technical?? 0.0f }",
                            Font = new FontUsage("", 13),
                            Colour = SkillsetColors.GetValueOrDefault("technical")
                        }
                    }
                }
            }
        };
    }

    private Drawable CreatePauseInfoSection()
    {
        return new FillFlowContainer
        {
            AutoSizeAxes = Axes.X,
            RelativeSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 4),
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = "PAUSE",
                    Font = new FontUsage("", 15, "Bold"),
                    Colour = new Color4(180, 180, 220, 255)
                },
                new SpriteText
                {
                    Text = $"{_entry.PauseDurationSeconds:F1} seconds",
                    Font = new FontUsage("", 17),
                    Colour = new Color4(140, 140, 180, 255)
                }
            }
        };
    }

    private Drawable CreateActionButtonsSection()
    {
        var children = new List<Drawable>();

        // Only show rate input for non-pause entries
        if (!_entry.IsPause)
        {
            children.Add(new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(4, 0),
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Children = new Drawable[]
                {
                    _rateTextBox = new StyledTextBox
                    {
                        Size = new Vector2(50, 24),
                        Text = _entry.Rate.ToString("0.0#", CultureInfo.InvariantCulture),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft
                    }
                }
            });
        }
        else
        {
            // For pause entries, create a dummy textbox (not displayed but needed to avoid null reference)
            _rateTextBox = new StyledTextBox { Alpha = 0, Size = Vector2.Zero };
        }

        children.Add(new ActionButton("\u2191", OnMoveUp, new Color4(70, 70, 75, 255), new Color4(90, 90, 95, 255))
        {
            Size = new Vector2(32, 28)
        });
        children.Add(new ActionButton("\u2193", OnMoveDown, new Color4(70, 70, 75, 255), new Color4(90, 90, 95, 255))
        {
            Size = new Vector2(32, 28)
        });
        children.Add(new ActionButton("\u2190", OnDelete, _deleteBg, _deleteHoverBg)
        {
            Size = new Vector2(36, 28)
        });

        return new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(4, 0),
            Anchor = Anchor.CentreRight,
            Origin = Anchor.CentreRight,
            Padding = new MarginPadding { Right = 8 },
            Children = children
        };
    }

    private void OnRateCommit(TextBox sender, bool newText)
    {
        if (double.TryParse(sender.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            var clampedRate = Math.Clamp(value, 0.1, 5.0);
            sender.Text = clampedRate.ToString("0.0#", CultureInfo.InvariantCulture);
            RateChanged?.Invoke(_entry, clampedRate);
        }
        else
        {
            sender.Text = _entry.Rate.ToString("0.0#", CultureInfo.InvariantCulture);
        }
    }

    private void OnMoveUp() => MoveUpRequested?.Invoke(_entry);
    private void OnMoveDown() => MoveDownRequested?.Invoke(_entry);
    private void OnDelete() => DeleteRequested?.Invoke(_entry);

    /// <summary>
    /// Gets the color for the MSD value based on the dominant skillset.
    /// </summary>
    private Color4 GetMsdColor()
    {
        if (_entry.MsdValues == null)
            return SkillsetColors["unknown"];

        var (dominantSkillset, _) = _entry.MsdValues.GetDominantSkillset();
        return SkillsetColors.GetValueOrDefault(dominantSkillset.ToLowerInvariant(), SkillsetColors["unknown"]);
    }

    protected override bool OnHover(HoverEvent e)
    {
        _background.FadeColour(_entry.IsPause ? _pauseHoverBg : _hoverBg, 100);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _background.FadeColour(_entry.IsPause ? _pauseBg : _normalBg, 100);
        base.OnHoverLost(e);
    }
}

/// <summary>
/// Action button with customizable colors for list item actions.
/// </summary>
public partial class ActionButton : CompositeDrawable
{
    private readonly string _text;
    private readonly Action _action;
    private readonly Color4 _normalBg;
    private readonly Color4 _hoverBg;

    private Box _background = null!;

    public ActionButton(string text, Action action, Color4 normalBg, Color4 hoverBg)
    {
        _text = text;
        _action = action;
        _normalBg = normalBg;
        _hoverBg = hoverBg;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 4;

        InternalChildren = new Drawable[]
        {
            _background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = _normalBg
            },
            new SpriteText
            {
                Text = _text,
                Font = new FontUsage("", 14, "Bold"),
                Colour = Color4.White,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            }
        };
    }

    protected override bool OnHover(HoverEvent e)
    {
        _background.FadeColour(_hoverBg, 100);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        _background.FadeColour(_normalBg, 100);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        _action?.Invoke();
        _background.FlashColour(Color4.White, 100);
        return true;
    }
}
