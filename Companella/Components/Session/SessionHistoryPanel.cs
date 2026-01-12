using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using osuTK.Graphics;
using Companella.Models;
using Companella.Services;
using Companella.Components.Charts;

namespace Companella.Components.Session;

/// <summary>
/// Panel for viewing and analyzing historical session data.
/// </summary>
public partial class SessionHistoryPanel : CompositeDrawable
{
    [Resolved]
    private SessionDatabaseService DatabaseService { get; set; } = null!;
    
    [Resolved]
    private SessionTrackerService TrackerService { get; set; } = null!;
    
    private SessionHistoryChart _historyChart = null!;
    private SessionDropdown _sessionDropdown = null!;
    private FillFlowContainer _statsContainer = null!;
    private SpriteText _totalPlaysText = null!;
    private SpriteText _avgAccuracyText = null!;
    private SpriteText _bestAccuracyText = null!;
    private SpriteText _worstAccuracyText = null!;
    private SpriteText _avgMsdText = null!;
    private SpriteText _durationText = null!;
    private SpriteText _noSessionsText = null!;
    private Container _skillsetContainer = null!;
    private HistoryDeleteButton _deleteButton = null!;
    
    private List<StoredSession> _sessions = new();
    private StoredSession? _selectedSession;
    
    private readonly Color4 _accentColor = new Color4(255, 102, 170, 255);
    
    public SessionHistoryPanel()
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
                Spacing = new Vector2(0, 10),
                Children = new Drawable[]
                {
                    // Header
                    new SpriteText
                    {
                        Text = "Session History",
                        Font = new FontUsage("", 17, "Bold"),
                        Colour = new Color4(180, 180, 180, 255)
                    },
                    // Session selector row
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
                                Text = "Session:",
                                Font = new FontUsage("", 15),
                                Colour = new Color4(160, 160, 160, 255),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            },
                            _sessionDropdown = new SessionDropdown
                            {
                                Width = 280,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft
                            },
                            _deleteButton = new HistoryDeleteButton
                            {
                                Size = new Vector2(70, 28),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Alpha = 0
                            }
                        }
                    },
                    // No sessions message
                    _noSessionsText = new SpriteText
                    {
                        Text = "No sessions recorded yet. Start tracking to record sessions!",
                        Font = new FontUsage("", 15),
                        Colour = new Color4(120, 120, 120, 255),
                        Alpha = 0
                    },
                    // Stats row
                    _statsContainer = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 6),
                        Alpha = 0,
                        Children = new Drawable[]
                        {
                            // Row 1: Plays and Duration
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(20, 0),
                                Children = new Drawable[]
                                {
                                    CreateStatItem("Plays:", out _totalPlaysText),
                                    CreateStatItem("Duration:", out _durationText),
                                    CreateStatItem("Avg MSD:", out _avgMsdText)
                                }
                            },
                            // Row 2: Accuracy stats
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(20, 0),
                                Children = new Drawable[]
                                {
                                    CreateStatItem("Avg Acc:", out _avgAccuracyText),
                                    CreateStatItem("Best:", out _bestAccuracyText),
                                    CreateStatItem("Worst:", out _worstAccuracyText)
                                }
                            },
                            // Skillset distribution
                            _skillsetContainer = new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Margin = new MarginPadding { Top = 4 }
                            }
                        }
                    },
                    // Chart
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 200,
                        Masking = true,
                        CornerRadius = 6,
                        Child = _historyChart = new SessionHistoryChart
                        {
                            RelativeSizeAxes = Axes.Both
                        }
                    }
                }
            }
        };
        
        _sessionDropdown.Current.ValueChanged += e => OnSessionSelected(e.NewValue);
        _deleteButton.Clicked += OnDeleteClicked;
        
        // Subscribe to session tracker events to auto-refresh when sessions are saved
        TrackerService.SessionStopped += OnTrackerSessionStopped;
        
        // Load sessions
        RefreshSessions();
    }
    
    private void OnTrackerSessionStopped(object? sender, EventArgs e)
    {
        // Schedule the refresh on the main thread
        Schedule(RefreshSessions);
    }
    
    /// <summary>
    /// Refreshes the list of sessions from the database.
    /// </summary>
    public void RefreshSessions()
    {
        _sessions = DatabaseService.GetSessions();
        
        _sessionDropdown.Items = _sessions;
        
        if (_sessions.Count == 0)
        {
            _noSessionsText.FadeTo(1, 200);
            _statsContainer.FadeTo(0, 200);
            _deleteButton.FadeTo(0, 200);
            _historyChart.Clear();
            _selectedSession = null;
        }
        else
        {
            _noSessionsText.FadeTo(0, 200);
            
            // Select the most recent session by default
            if (_selectedSession == null || !_sessions.Any(s => s.Id == _selectedSession.Id))
            {
                _sessionDropdown.Current.Value = _sessions.First();
            }
        }
    }
    
    private void OnSessionSelected(StoredSession? session)
    {
        if (session == null)
        {
            _selectedSession = null;
            _statsContainer.FadeTo(0, 200);
            _deleteButton.FadeTo(0, 200);
            _historyChart.Clear();
            return;
        }
        
        // Load full session with plays
        _selectedSession = DatabaseService.GetSessionById(session.Id);
        
        if (_selectedSession == null)
        {
            _statsContainer.FadeTo(0, 200);
            _deleteButton.FadeTo(0, 200);
            _historyChart.Clear();
            return;
        }
        
        // Update stats
        _totalPlaysText.Text = _selectedSession.TotalPlays.ToString();
        _durationText.Text = _selectedSession.Duration.ToString(@"hh\:mm\:ss");
        _avgMsdText.Text = $"{_selectedSession.AverageMsd:F1}";
        _avgAccuracyText.Text = $"{_selectedSession.AverageAccuracy:F2}%";
        _bestAccuracyText.Text = $"{_selectedSession.BestAccuracy:F2}%";
        _worstAccuracyText.Text = $"{_selectedSession.WorstAccuracy:F2}%";
        
        // Update skillset distribution
        UpdateSkillsetDistribution();
        
        // Show stats and delete button
        _statsContainer.FadeTo(1, 200);
        _deleteButton.FadeTo(1, 200);
        
        // Update chart
        _historyChart.SetSession(_selectedSession);
    }
    
    private void UpdateSkillsetDistribution()
    {
        _skillsetContainer.Clear();
        
        if (_selectedSession == null || _selectedSession.Plays.Count == 0)
            return;
        
        var distribution = _selectedSession.GetSkillsetDistribution();
        if (distribution.Count == 0)
            return;
        
        var flowContainer = new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(8, 0),
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = "Skillsets:",
                    Font = new FontUsage("", 14),
                    Colour = new Color4(140, 140, 140, 255),
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft
                }
            }
        };
        
        foreach (var (skillset, count) in distribution.OrderByDescending(kvp => kvp.Value))
        {
            var color = SessionHistoryChart.SkillsetColors.GetValueOrDefault(
                skillset.ToLowerInvariant(), 
                SessionHistoryChart.SkillsetColors["unknown"]);
            
            flowContainer.Add(new SkillsetBadge(skillset, count, color));
        }
        
        _skillsetContainer.Add(flowContainer);
    }
    
    private void OnDeleteClicked()
    {
        if (_selectedSession == null)
            return;
        
        var sessionId = _selectedSession.Id;
        
        if (DatabaseService.DeleteSession(sessionId))
        {
            Logger.Info($"[History] Deleted session {sessionId}");
            RefreshSessions();
        }
    }
    
    private FillFlowContainer CreateStatItem(string label, out SpriteText valueText)
    {
        valueText = new SpriteText
        {
            Text = "-",
            Font = new FontUsage("", 15),
            Colour = _accentColor,
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft
        };
        
        return new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(4, 0),
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = label,
                    Font = new FontUsage("", 14),
                    Colour = new Color4(140, 140, 140, 255),
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft
                },
                valueText
            }
        };
    }
}

/// <summary>
/// Dropdown for selecting sessions.
/// </summary>
public partial class SessionDropdown : BasicDropdown<StoredSession?>
{
    public SessionDropdown()
    {
    }
    
    protected override LocalisableString GenerateItemText(StoredSession? item)
    {
        return item?.DisplayName ?? "Select a session...";
    }
}

/// <summary>
/// Small badge showing skillset count.
/// </summary>
public partial class SkillsetBadge : CompositeDrawable
{
    public SkillsetBadge(string skillset, int count, Color4 color)
    {
        AutoSizeAxes = Axes.Both;
        
        InternalChild = new Container
        {
            AutoSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 4,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(color.R, color.G, color.B, 0.3f)
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Padding = new MarginPadding { Horizontal = 6, Vertical = 2 },
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = $"{skillset}: ",
                            Font = new FontUsage("", 13),
                            Colour = color
                        },
                        new SpriteText
                        {
                            Text = count.ToString(),
                            Font = new FontUsage("", 13, "Bold"),
                            Colour = color
                        }
                    }
                }
            }
        };
    }
}

/// <summary>
/// Delete button for sessions.
/// </summary>
public partial class HistoryDeleteButton : CompositeDrawable
{
    private Box _background = null!;
    private Box _hoverOverlay = null!;
    
    private readonly Color4 _deleteColor = new Color4(200, 80, 80, 255);
    
    public event Action? Clicked;
    
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
                Colour = _deleteColor
            },
            _hoverOverlay = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0
            },
            new SpriteText
            {
                Text = "Delete",
                Font = new FontUsage("", 17, "Bold"),
                Colour = Color4.White,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            }
        };
    }
    
    protected override bool OnHover(HoverEvent e)
    {
        _hoverOverlay.FadeTo(0.15f, 100);
        return base.OnHover(e);
    }
    
    protected override void OnHoverLost(HoverLostEvent e)
    {
        _hoverOverlay.FadeTo(0, 100);
        base.OnHoverLost(e);
    }
    
    protected override bool OnClick(ClickEvent e)
    {
        _hoverOverlay.FadeTo(0.3f, 50).Then().FadeTo(0.15f, 100);
        Clicked?.Invoke();
        return true;
    }
}

