# ChatFont

Every character picks a chat font in the character creator: the Chat font dropdown in the Body card
(`Content.Client/_WF/ChatFont/HumanoidProfileEditor.ChatFont.cs`), with a sample line drawn in the pick. On offer are
Default, Terminal (Roboto Mono), Boxfont Round, Iceberg, Fondamento and Minecraftia, the `wfChatFont` prototypes in
`Resources/Prototypes/_WF/ChatFont/chat_fonts.yml`; each names the client `font` prototype it draws in. Language
fonts (Octatrax, Scratch, Thaveyan) are not offered.

The profile stores the pick as `ChatFont` (`HumanoidCharacterProfile.ChatFont.cs`, database column `chat_font`);
validation turns an unknown id into `WFChatFontDefault`. When a profile is loaded onto a mob, `WFChatFontSystem` gives
it `WFChatFontComponent` with the client font id, or takes it off for the default. The chat wraps read it in
`ChatSystem.WrapMessage` (say and whisper), `RadioSystem.WrapRadioMessage` and the telephone wrap: a language's own
font comes first, then the picked font, then the speech verb's. Speech bubbles draw the same wrapped markup, so they
follow it.
