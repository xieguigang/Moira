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
            public const uint cmdTabPage = 3;
            public const uint cmdCommandGroup = 4;
            public const uint cmdCommandPanel = 5;
            public const uint cmdButtonReset = 2;
        }

        // ContextPopup CommandName

        public Ribbon Ribbon { get; private set; }
        public RibbonTabGroup TabPage { get; private set; }
        public RibbonTab CommandGroup { get; private set; }
        public RibbonGroup CommandPanel { get; private set; }
        public RibbonButton ButtonReset { get; private set; }

        public RibbonItems(Ribbon ribbon)
        {
            if (ribbon == null)
                throw new ArgumentNullException(nameof(ribbon), "Parameter is null");
            this.Ribbon = ribbon;
            TabPage = new RibbonTabGroup(ribbon, Cmd.cmdTabPage);
            CommandGroup = new RibbonTab(ribbon, Cmd.cmdCommandGroup);
            CommandPanel = new RibbonGroup(ribbon, Cmd.cmdCommandPanel);
            ButtonReset = new RibbonButton(ribbon, Cmd.cmdButtonReset);
        }

    }
}