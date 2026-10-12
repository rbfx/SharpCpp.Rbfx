using Ea;
using ImGuiNet;
using System.Globalization;
using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.Graphics;
using Urho3D.Input;
using Urho3D.Math;
using Urho3D.Resource;
using Urho3D.Scene;

namespace Rbfx.Examples.Common;

/// <summary>
/// Demo application with a 3D scene, an RmlUI dashboard and a Dear ImGui window.
/// The cube rotates with the native RotatingComponent when the user code is available.
/// Otherwise the cube rotates in managed code, in the update event.
/// </summary>
public sealed partial class DemoApplication : Application
{
    private const float CubeRotationSpeed = 45.0f;
    private const float CameraAimOffset = 2.0f;
    private const string NativeDriverText = "Native RotatingComponent";
    private const string ManagedDriverText = "Managed OnUpdate";

    private readonly string? _logName;
    private readonly Ea.String _documentSource = new(DemoDocument.Source);
    private readonly byte[] _imguiWindowTitle = "SharpCpp ImGui Demo"u8.ToArray();
    private readonly byte[] _imguiIntroHeader = "Dear ImGui"u8.ToArray();
    private readonly byte[] _imguiIntroText = "Generated from imgui.h through SharpCpp. This window uses source-driven Dear ImGui bindings."u8.ToArray();
    private readonly byte[] _imguiNoteHeader = "Operator note"u8.ToArray();
    private readonly byte[] _imguiToggleTooltip = "Toggles extra Dear ImGui details for this sample window."u8.ToArray();

    private SceneSample? _sample;
    private Viewport? _viewport;
    private Node _cube = null!;
    private Urho3D.RmlUI.RmlUI _ui = null!;
    private Rml.Context _rmlContext = null!;
    private Rml.DataModelConstructor? _dataModelConstructor;
    private Rml.DataModelHandle? _dataModelHandle;
    private Rml.ElementDocument? _document;
    private bool _nativeRotation;
    private float _managedAngle;
    private float _elapsedTime;
    private float _cubeRotation;
    private readonly Ea.String _elapsedText = new("0.0 s");
    private readonly Ea.String _rotationText = new("0 deg");
    private readonly Ea.String _driverText = new(ManagedDriverText);
    private readonly Ea.String _fpsText = new("--");
    private readonly Ea.String _statusText = new("Initializing");
    private readonly Ea.String _noteText = new("Bindings online");
    private float _framesPerSecond;
    private bool _hasFrameRate;
    private int _eventCount;
    private bool _showImGuiDetails = true;
    private readonly Ea.String _imguiElapsedLabel = new(string.Empty);
    private readonly Ea.String _imguiRotationLabel = new(string.Empty);
    private readonly Ea.String _imguiDriverLabel = new(string.Empty);
    private readonly Ea.String _imguiFpsLabel = new(string.Empty);
    private readonly Ea.String _imguiStatusLabel = new(string.Empty);
    private readonly Ea.String _imguiNoteValue = new(string.Empty);
    private readonly Ea.String _imguiToggleLabel = new(string.Empty);
    private readonly Ea.String _imguiDetailsText = new(string.Empty);

    /// <summary>Creates the application.</summary>
    /// <param name="context">The engine context.</param>
    /// <param name="logName">Path of the log file, or null for the engine default.</param>
    public DemoApplication(Context context, string? logName = null) : base(context)
    {
        _logName = logName;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseSceneAndUi();
            _documentSource.Dispose();
            _elapsedText.Dispose();
            _rotationText.Dispose();
            _driverText.Dispose();
            _fpsText.Dispose();
            _statusText.Dispose();
            _noteText.Dispose();
            _imguiElapsedLabel.Dispose();
            _imguiRotationLabel.Dispose();
            _imguiDriverLabel.Dispose();
            _imguiFpsLabel.Dispose();
            _imguiStatusLabel.Dispose();
            _imguiNoteValue.Dispose();
            _imguiToggleLabel.Dispose();
            _imguiDetailsText.Dispose();
        }

        base.Dispose(disposing);
    }

    public override void Setup()
    {
        EngineParameters[EngineDefs.WindowTitle] = "rbfx Examples - RmlUI + ImGui Demo";
        EngineParameters[EngineDefs.ApplicationName] = "Rbfx.Examples";
        EngineParameters[EngineDefs.WindowWidth] = 1280;
        EngineParameters[EngineDefs.WindowHeight] = 720;
        EngineParameters[EngineDefs.FullScreen] = false;
        EngineParameters[EngineDefs.Borderless] = false;
        EngineParameters[EngineDefs.EngineAutoLoadScripts] = false;
        EngineParameters[EngineDefs.ResourcePrefixPaths] = AppContext.BaseDirectory;
        EngineParameters[EngineDefs.ResourcePaths] = "CoreData;Data";
        if (_logName != null)
        {
            EngineParameters[EngineDefs.LogName] = _logName;
        }
    }

    public override void Start()
    {
        var context = GetContext();
        var input = Require(context.GetSubsystem<Input>(), "input");
        input.SetMouseMode(MouseMode.MmFree);
        input.SetMouseVisible(true);

        _sample = new SceneSample(context);
        var scene = _sample.Scene;
        _cube = _sample.PrimaryNode;
        _nativeRotation = _sample.UserCodeDescription != null;
        _driverText.Assign(_nativeRotation ? NativeDriverText : ManagedDriverText);
        Console.WriteLine(UserCodeSupport.IsAvailable
            ? $"Example.UserCode is active: {_sample.UserCodeDescription ?? "the native component was not created"}"
            : "Example.UserCode is not available in this build.");

        scene.CreateComponent<Octree>();
        var cache = Require(context.GetSubsystem<ResourceCache>(), "resource cache");
        var model = Require(_cube.CreateComponent<StaticModel>(), "cube model component");
        model.SetModel(Require(cache.GetResource<Model>("Models/Box.mdl"), "box model"));
        model.SetMaterial(0, Require(cache.GetResource<Material>("Materials/StoneTiled.xml"), "stone material"));

        var camera = Require(scene.CreateChild("Camera"), "camera node");
        camera.Position = _cube.Position + new Vector3(0, 1.5f, -4);
        // Aim left of the cube. The cube appears on the right, beside the UI panels.
        camera.LookAt(_cube.Position + new Vector3(-CameraAimOffset, 0, 0));
        _viewport = new Viewport(context)
        {
            Scene = scene,
            Camera = Require(camera.CreateComponent<Camera>(), "camera component")
        };
        var renderer = Require(context.GetSubsystem<Renderer>(), "renderer");
        renderer.SetViewport(0, _viewport);
        renderer.DefaultZone.FogColor = new Color(0.15f, 0.2f, 0.3f);

        var light = Require(scene.CreateChild("Light"), "light node");
        light.Position = _cube.Position + new Vector3(0, 3, -3);
        light.LookAt(_cube.Position);
        light.CreateComponent<Light>();

        InitializeRmlUi();

        SubscribeToEvent(CoreEvents.Update, OnUpdate);
    }

    public override void Stop() => ReleaseSceneAndUi();

    private void ReleaseSceneAndUi()
    {
        _document?.CloseDocument();
        _document?.Dispose();
        _document = null;
        _dataModelHandle?.Dispose();
        _dataModelHandle = null;
        _dataModelConstructor?.Dispose();
        _dataModelConstructor = null;
        _viewport?.Dispose();
        _viewport = null;
        _sample?.Dispose();
        _sample = null;
    }

    private void OnUpdate(Urho3D.Core.Object sender, StringHash eventType, Ea.UnorderedMap<StringHash, Variant> eventData)
    {
        var timeStep = eventData.TryGetValue(CoreEvents.Update.TimeStep, out var value)
            ? value.Float
            : 0.0f;
        if (!_nativeRotation)
        {
            _managedAngle = (_managedAngle + CubeRotationSpeed * timeStep) % 360.0f;
            _cube.Rotation = new Quaternion(0, _managedAngle, 0);
        }

        UpdateDashboard(timeStep);
    }

    private void InitializeRmlUi()
    {
        _ui = Require(GetContext().GetSubsystem<Urho3D.RmlUI.RmlUI>(), "RmlUI subsystem");

        if (!_ui.LoadFont("Fonts/NotoSans-Condensed.ttf") || !_ui.LoadFont("Fonts/NotoSans-CondensedBold.ttf"))
        {
            throw new InvalidOperationException("Failed to load the fonts required by the RmlUI demo.");
        }

        _rmlContext = _ui.RmlContext;
        InitializeDataModel();

        using var documentMarkup = new Ea.String(DemoDocument.Markup);
        _document = Require(_rmlContext.LoadDocumentFromMemory(documentMarkup, _documentSource), "in-memory RmlUI document");

        _document.Show();
        UpdateImGuiTextState();
    }

    private void InitializeDataModel()
    {
        using var modelName = new Ea.String(DemoDocument.DataModelName);
        using var elapsedName = new Ea.String("elapsed");
        using var rotationName = new Ea.String("rotation");
        using var driverName = new Ea.String("driver");
        using var fpsName = new Ea.String("fps");
        using var statusName = new Ea.String("status");
        using var noteName = new Ea.String("note");
        using var applyNoteName = new Ea.String("apply_note");

        var constructor = Require(_rmlContext.CreateDataModel(modelName), "RmlUI data model");
        _dataModelConstructor = constructor;

        BindTextValue(elapsedName, _elapsedText);
        BindTextValue(rotationName, _rotationText);
        BindTextValue(driverName, _driverText);
        BindTextValue(fpsName, _fpsText);
        BindTextValue(statusName, _statusText);
        BindMutableTextValue(noteName, _noteText);
        BindRequired(applyNoteName, ApplyNoteFromEvent);

        _dataModelHandle = Require(constructor.GetModelHandle(), "RmlUI data model handle");
    }

    private void UpdateDashboard(float timeStep)
    {
        _elapsedTime += timeStep;
        // The native RotatingComponent starts with the first update and uses the same speed.
        // The angle is computed. It is not read from Node.Rotation.
        _cubeRotation = _nativeRotation ? _elapsedTime * CubeRotationSpeed % 360.0f : _managedAngle;

        UpdateModelValue(_elapsedText, $"{_elapsedTime.ToString("0.0", CultureInfo.InvariantCulture)} s", "elapsed");
        UpdateModelValue(_rotationText, $"{_cubeRotation.ToString("0", CultureInfo.InvariantCulture)} deg", "rotation");
        _hasFrameRate = timeStep > 0.0f;
        _framesPerSecond = _hasFrameRate ? 1.0f / timeStep : 0.0f;

        UpdateModelValue(_fpsText, _hasFrameRate
            ? $"{_framesPerSecond.ToString("0", CultureInfo.InvariantCulture)} fps"
            : "--",
            "fps");

        using var noteName = new Ea.String("note");
        var noteDirty = _dataModelHandle != null && _dataModelHandle.IsVariableDirty(noteName);
        var statusText = _eventCount > 0
            ? $"{_eventCount.ToString(CultureInfo.InvariantCulture)} event callback(s) handled"
            : noteDirty
                ? (IsNullOrWhiteSpace(_noteText) ? "Note cleared through the data model" : "Note updated through the data model")
                : (_ui.IsHovered ? "Pointer over UI" : "Pointer free for the 3D scene");

        UpdateModelValue(_statusText, statusText, "status");
        UpdateImGuiTextState();
        DrawImGui();
    }

    private void DrawImGui()
    {
        ImGui.SetNextWindowPos(new Vector2(430.0f, 28.0f), ImGuiCond.Once);
        ImGui.SetNextWindowSize(new Vector2(380.0f, 270.0f), ImGuiCond.Once);

        var renderBody = ImGui.Begin(_imguiWindowTitle, out _);

        try
        {
            if (!renderBody)
            {
                return;
            }

            ImGui.SeparatorText(_imguiIntroHeader);
            ImGui.PushTextWrapPos();
            try
            {
                ImGui.TextUnformatted(_imguiIntroText);
            }
            finally
            {
                ImGui.PopTextWrapPos();
            }

            ImGui.TextUnformatted(_imguiElapsedLabel);
            ImGui.TextUnformatted(_imguiRotationLabel);
            ImGui.TextUnformatted(_imguiDriverLabel);
            ImGui.TextUnformatted(_imguiFpsLabel);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.56f, 0.78f, 0.92f, 1.0f));
            try
            {
                ImGui.TextUnformatted(_imguiStatusLabel);
            }
            finally
            {
                ImGui.PopStyleColor();
            }

            ImGui.SeparatorText(_imguiNoteHeader);
            ImGui.PushTextWrapPos();
            try
            {
                ImGui.TextUnformatted(_imguiNoteValue);
            }
            finally
            {
                ImGui.PopTextWrapPos();
            }

            if (ImGui.Button(_imguiToggleLabel))
            {
                _showImGuiDetails = !_showImGuiDetails;
                UpdateImGuiTextState();
            }

            if (ImGui.BeginItemTooltip())
            {
                try
                {
                    ImGui.TextUnformatted(_imguiToggleTooltip);
                }
                finally
                {
                    ImGui.EndTooltip();
                }
            }

            if (_showImGuiDetails)
            {
                ImGui.Separator();
                ImGui.PushTextWrapPos();
                try
                {
                    ImGui.TextUnformatted(_imguiDetailsText);
                }
                finally
                {
                    ImGui.PopTextWrapPos();
                }
            }
        }
        finally
        {
            ImGui.End();
        }
    }

    private void UpdateImGuiTextState()
    {
        _imguiElapsedLabel.Format($"Elapsed: {_elapsedTime:F1} s");
        _imguiRotationLabel.Format($"Rotation: {_cubeRotation:F0} deg");
        _imguiDriverLabel.Format($"Rotation driver: {_driverText}");
        if (_hasFrameRate)
        {
            _imguiFpsLabel.Format($"Frame rate: {_framesPerSecond:F0} fps");
        }
        else
        {
            _imguiFpsLabel.Format($"Frame rate: --");
        }

        _imguiStatusLabel.Format($"Status: {_statusText}");
        _imguiNoteValue.Format($"{(IsNullOrWhiteSpace(_noteText) ? "(empty)" : _noteText)}");
        _imguiToggleLabel.Format($"{(_showImGuiDetails ? "Hide extra ImGui details" : "Show extra ImGui details")}");
        _imguiDetailsText.Format(
            $"UI hovered: {(_ui != null && _ui.IsHovered ? "yes" : "no")} | Event callbacks: {_eventCount} | Note length: {_noteText.Count}");
    }

    private void BindRequired(Ea.String name, Action<Rml.Variant> getFunc)
    {
        if (!Constructor.BindFunc(name, getFunc))
        {
            throw new InvalidOperationException($"Failed to bind RmlUI data-model variable '{name}'.");
        }
    }

    private void BindRequired(Ea.String name, Action<Rml.Variant> getFunc, Action<Rml.Variant> setFunc)
    {
        if (!Constructor.BindFunc(name, getFunc, setFunc))
        {
            throw new InvalidOperationException($"Failed to bind RmlUI data-model variable '{name}'.");
        }
    }

    private void BindRequired(Ea.String name, Action<Rml.DataModelHandle, Rml.Event, Ea.Vector<Rml.Variant>> eventFunc)
    {
        if (!Constructor.BindEventCallback(name, eventFunc))
        {
            throw new InvalidOperationException($"Failed to bind RmlUI data-model event '{name}'.");
        }
    }

    private void BindTextValue(Ea.String name, Ea.String text)
    {
        BindRequired(name, value =>
        {
            _ = value.Assign<Ea.String>(text);
        });
    }

    private void BindMutableTextValue(Ea.String name, Ea.String text)
    {
        BindRequired(
            name,
            value =>
            {
                _ = value.Assign<Ea.String>(text);
            },
            value => text.Assign(value.Get<Ea.String>()));
    }

    private void UpdateModelValue(Ea.String currentValue, string newValue, string variableName)
    {
        if (currentValue.Equals(newValue) || _dataModelHandle == null)
        {
            return;
        }

        currentValue.Assign(newValue);
        using var nativeVariableName = new Ea.String(variableName);
        _dataModelHandle.DirtyVariable(nativeVariableName);
    }

    private void ApplyNoteFromEvent(Rml.DataModelHandle handle, Rml.Event @event, Ea.Vector<Rml.Variant> arguments)
    {
        _ = @event;

        if (arguments.Count > 0)
        {
            _noteText.Assign(arguments[0].Get<Ea.String>());
        }
        else
        {
            _noteText.Assign(string.Empty);
        }

        _eventCount++;
        handle.DirtyVariable("note");
        handle.DirtyVariable("status");
    }

    private static bool IsNullOrWhiteSpace(Ea.String value)
        => string.IsNullOrWhiteSpace(value.ToString());
    private Rml.DataModelConstructor Constructor
        => _dataModelConstructor ?? throw new InvalidOperationException("The RmlUI data model is not created.");

    private static T Require<T>(T? value, string name) where T : class
        => value ?? throw new InvalidOperationException($"Could not create or load {name}.");
}
