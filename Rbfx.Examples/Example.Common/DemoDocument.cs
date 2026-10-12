namespace Rbfx.Examples.Common;

/// <summary>The RmlUI document of <see cref="DemoApplication"/>. It is loaded from memory.</summary>
public static class DemoDocument
{
    /// <summary>Virtual source name of the in-memory document.</summary>
    public const string Source = "memory://rbfx.examples/demo.rml";

    /// <summary>Name of the data model that the markup uses.</summary>
    public const string DataModelName = "dashboard";

    /// <summary>RML markup. The data model variables are elapsed, rotation, driver, fps, status and note.</summary>
    public const string Markup = """
    <rml>
    <head>
        <title>SharpCpp RmlUI Demo</title>
        <style>
            body {
                margin: 0;
                font-family: "Noto Sans";
                color: #f5f7fa;
            }

            .panel {
                display: block;
                position: absolute;
                top: 28px;
                left: 28px;
                width: 360px;
                padding: 14px 22px 12px;
                background-color: #182430dd;
            }

            .eyebrow {
                display: block;
                color: #8fc7ea;
                font-size: 13px;
                text-transform: uppercase;
                letter-spacing: 1px;
                margin-bottom: 8px;
            }

            .title {
                display: block;
                margin-bottom: 6px;
                font-size: 24px;
                font-weight: bold;
            }

            .copy {
                display: block;
                margin-bottom: 10px;
                color: #d0d8e2;
                font-size: 14px;
            }

            .metrics {
                display: block;
                margin-bottom: 8px;
            }

            .metric {
                display: block;
                margin-bottom: 5px;
                padding: 5px 14px 6px;
                background-color: #223548;
            }

            .metric-label {
                display: block;
                color: #8ea9bf;
                font-size: 12px;
                margin-bottom: 1px;
                text-transform: uppercase;
                letter-spacing: 1px;
            }

            .metric-value {
                display: block;
                font-size: 18px;
                font-weight: bold;
            }

            .editor-label {
                display: block;
                margin-bottom: 6px;
                color: #8ea9bf;
                font-size: 12px;
                text-transform: uppercase;
                letter-spacing: 1px;
            }

            .note-input {
                display: block;
                width: 100%;
                margin-bottom: 6px;
                padding: 6px 10px;
                background-color: #0f1923;
                border-width: 1px;
                border-color: #39556d;
                color: #f5f7fa;
                font-size: 15px;
            }

            .note-preview {
                display: block;
                margin-bottom: 8px;
                color: #d0d8e2;
                font-size: 14px;
            }

            .actions {
                display: block;
                margin-bottom: 8px;
            }

            .action-button {
                display: block;
                width: 100%;
                margin-bottom: 6px;
                padding: 7px 10px;
                background-color: #2b7ea1;
                border-width: 1px;
                border-color: #54a9cd;
                color: #f5f7fa;
                font-size: 14px;
                font-weight: bold;
            }

            .action-button.secondary {
                margin-bottom: 0;
                background-color: #1b2c39;
                border-color: #39556d;
            }

            .hint {
                display: block;
                color: #8fc7ea;
                font-size: 14px;
            }
        </style>
    </head>
    <body>
        <div class="panel" data-model="dashboard">
            <div class="eyebrow">RmlUI Overlay</div>
            <div class="title">Rbfx Examples</div>
            <div class="copy">This document is created from memory and driven through Rml data-model callbacks implemented in C#.</div>

            <div class="metrics">
                <div class="metric">
                    <div class="metric-label">Elapsed</div>
                    <div class="metric-value">{{elapsed}}</div>
                </div>
                <div class="metric">
                    <div class="metric-label">Cube rotation</div>
                    <div class="metric-value">{{rotation}}</div>
                </div>
                <div class="metric">
                    <div class="metric-label">Rotation driver</div>
                    <div class="metric-value">{{driver}}</div>
                </div>
                <div class="metric">
                    <div class="metric-label">Frame rate</div>
                    <div class="metric-value">{{fps}}</div>
                </div>
                <div class="metric">
                    <div class="metric-label">Status</div>
                    <div class="metric-value">{{status}}</div>
                </div>
            </div>

            <div class="editor-label">Operator note</div>
            <input class="note-input" type="text" data-value="note"/>
            <div class="note-preview">Echo: {{note}}</div>

            <div class="actions">
                <button class="action-button" data-event-click="apply_note('Pinned from event callback')">Apply preset note</button>
                <button class="action-button secondary" data-event-click="apply_note('')">Clear note</button>
            </div>

            <div class="hint">Edit the note or press the buttons to exercise both `BindFunc` and `BindEventCallback` from C#.</div>
        </div>
    </body>
    </rml>
    """;
}
