using Robust.Shared.Input;

namespace Content.Shared._WF.MappingMode;

/// <summary>Keys of the mapping screen: the build modes, the mode's mouse buttons and the template nudges.</summary>
[KeyFunctions]
public static class WFMappingKeyFunctions
{
    public static readonly BoundKeyFunction WFMappingMode1 = "WFMappingMode1";
    public static readonly BoundKeyFunction WFMappingMode2 = "WFMappingMode2";
    public static readonly BoundKeyFunction WFMappingMode3 = "WFMappingMode3";
    public static readonly BoundKeyFunction WFMappingMode4 = "WFMappingMode4";
    public static readonly BoundKeyFunction WFMappingMode5 = "WFMappingMode5";
    public static readonly BoundKeyFunction WFMappingMode6 = "WFMappingMode6";
    public static readonly BoundKeyFunction WFMappingMode7 = "WFMappingMode7";
    public static readonly BoundKeyFunction WFMappingModePrimary = "WFMappingModePrimary";
    public static readonly BoundKeyFunction WFMappingModeSecondary = "WFMappingModeSecondary";
    public static readonly BoundKeyFunction WFMappingModeMiddle = "WFMappingModeMiddle";
    public static readonly BoundKeyFunction WFMappingModeCancel = "WFMappingModeCancel";
    public static readonly BoundKeyFunction WFMappingTemplateNudgeUp = "WFMappingTemplateNudgeUp";
    public static readonly BoundKeyFunction WFMappingTemplateNudgeDown = "WFMappingTemplateNudgeDown";
    public static readonly BoundKeyFunction WFMappingTemplateNudgeLeft = "WFMappingTemplateNudgeLeft";
    public static readonly BoundKeyFunction WFMappingTemplateNudgeRight = "WFMappingTemplateNudgeRight";

    /// <summary>The mode keys, Place first.</summary>
    public static readonly BoundKeyFunction[] Modes =
    {
        WFMappingMode1,
        WFMappingMode2,
        WFMappingMode3,
        WFMappingMode4,
        WFMappingMode5,
        WFMappingMode6,
        WFMappingMode7,
    };

    /// <summary>The template's arrow keys, added while Template mode is on.</summary>
    public static readonly BoundKeyFunction[] Nudges =
    {
        WFMappingTemplateNudgeUp,
        WFMappingTemplateNudgeDown,
        WFMappingTemplateNudgeLeft,
        WFMappingTemplateNudgeRight,
    };
}
