//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

using System;
using RibbonLib;
using RibbonLib.Controls;
using RibbonLib.Interop;

namespace RibbonLib.Controls
{
    partial class RibbonItems
    {
        private static class Cmd
        {
            public const uint cmdFileOpen = 8;
            public const uint cmdButtonAbout = 12;
            public const uint cmdButtonAppExit = 13;
            public const uint cmdTabSimulationPage = 15;
            public const uint cmdGroupSimulation = 16;
            public const uint cmdPageSimulation = 17;
            public const uint cmdButtonSimulationStart = 18;
            public const uint cmdButtonSimulationPause = 19;
            public const uint cmdButtonSimulationStop = 20;
            public const uint cmdMenu2 = 10;
            public const uint cmdCheckShowTracer = 9;
            public const uint cmdCheckShowFlowLine = 11;
            public const uint cmdTabSimulationOperations = 22;
            public const uint cmdButtonClearBarrier = 6;
            public const uint cmdButtonReset = 2;
            public const uint cmdCheckDrawBarrier = 7;
            public const uint cmdTabApplicationMain = 21;
            public const uint cmdCommandGroup = 4;
            public const uint cmdFileNew = 23;
            public const uint cmdGroupApp = 14;
        }

        // ContextPopup CommandName

        public Ribbon Ribbon { get; private set; }
        public RibbonButton FileOpen { get; private set; }
        public RibbonButton ButtonAbout { get; private set; }
        public RibbonButton ButtonAppExit { get; private set; }
        public RibbonTabGroup TabSimulationPage { get; private set; }
        public RibbonTab GroupSimulation { get; private set; }
        public RibbonGroup PageSimulation { get; private set; }
        public RibbonButton ButtonSimulationStart { get; private set; }
        public RibbonButton ButtonSimulationPause { get; private set; }
        public RibbonButton ButtonSimulationStop { get; private set; }
        public RibbonGroup Menu2 { get; private set; }
        public RibbonToggleButton CheckShowTracer { get; private set; }
        public RibbonToggleButton CheckShowFlowLine { get; private set; }
        public RibbonGroup TabSimulationOperations { get; private set; }
        public RibbonButton ButtonClearBarrier { get; private set; }
        public RibbonButton ButtonReset { get; private set; }
        public RibbonToggleButton CheckDrawBarrier { get; private set; }
        public RibbonTab TabApplicationMain { get; private set; }
        public RibbonGroup CommandGroup { get; private set; }
        public RibbonButton FileNew { get; private set; }
        public RibbonGroup GroupApp { get; private set; }

        public RibbonItems(Ribbon ribbon)
        {
            if (ribbon == null)
                throw new ArgumentNullException(nameof(ribbon), "Parameter is null");
            this.Ribbon = ribbon;
            FileOpen = new RibbonButton(ribbon, Cmd.cmdFileOpen);
            ButtonAbout = new RibbonButton(ribbon, Cmd.cmdButtonAbout);
            ButtonAppExit = new RibbonButton(ribbon, Cmd.cmdButtonAppExit);
            TabSimulationPage = new RibbonTabGroup(ribbon, Cmd.cmdTabSimulationPage);
            GroupSimulation = new RibbonTab(ribbon, Cmd.cmdGroupSimulation);
            PageSimulation = new RibbonGroup(ribbon, Cmd.cmdPageSimulation);
            ButtonSimulationStart = new RibbonButton(ribbon, Cmd.cmdButtonSimulationStart);
            ButtonSimulationPause = new RibbonButton(ribbon, Cmd.cmdButtonSimulationPause);
            ButtonSimulationStop = new RibbonButton(ribbon, Cmd.cmdButtonSimulationStop);
            Menu2 = new RibbonGroup(ribbon, Cmd.cmdMenu2);
            CheckShowTracer = new RibbonToggleButton(ribbon, Cmd.cmdCheckShowTracer);
            CheckShowFlowLine = new RibbonToggleButton(ribbon, Cmd.cmdCheckShowFlowLine);
            TabSimulationOperations = new RibbonGroup(ribbon, Cmd.cmdTabSimulationOperations);
            ButtonClearBarrier = new RibbonButton(ribbon, Cmd.cmdButtonClearBarrier);
            ButtonReset = new RibbonButton(ribbon, Cmd.cmdButtonReset);
            CheckDrawBarrier = new RibbonToggleButton(ribbon, Cmd.cmdCheckDrawBarrier);
            TabApplicationMain = new RibbonTab(ribbon, Cmd.cmdTabApplicationMain);
            CommandGroup = new RibbonGroup(ribbon, Cmd.cmdCommandGroup);
            FileNew = new RibbonButton(ribbon, Cmd.cmdFileNew);
            GroupApp = new RibbonGroup(ribbon, Cmd.cmdGroupApp);
        }

    }
}
