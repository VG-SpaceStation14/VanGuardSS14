using System.Linq;
using System.Text;
using Content.Shared._VanGuard.Language;
using Content.Shared.Paper;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._VanGuard.Language;

/// <summary>
///     Validates language-tagged paper text on the server and prepares the text shown
///     to readers who do not understand a written language.
/// </summary>
public sealed partial class PaperLanguageSystem : EntitySystem
{
    [Dependency] private LanguageSystem _language = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PaperComponent, PaperWritingTextEvent>(OnPaperWriting);
    }

    private void OnPaperWriting(Entity<PaperComponent> entity, ref PaperWritingTextEvent args)
    {
        if (args.Segments.Count == 0 || args.Segments.Sum(segment => segment.Text.Length) != args.Text.Length)
        {
            args.Segments = new List<PaperComponent.PaperTextSegment>
            {
                CreateSegment(args.User, args.Text, SharedLanguageSystem.CommonLanguageId)
            };
            return;
        }

        var normalized = new List<PaperComponent.PaperTextSegment>();
        var textOffset = 0;

        foreach (var segment in args.Segments)
        {
            if (string.IsNullOrEmpty(segment.Text))
                continue;

            var language = segment.Language;
            // Only downgrade text the current writer actually added. Pre-existing
            // segments (already stored on the paper by another author) are kept as
            // they are: a second author may not speak the language, but must not be
            // able to make previously written text readable for everyone.
            if (!_language.CanSpeak(args.User, language) && !WasStoredOnPaper(entity, segment))
                language = SharedLanguageSystem.CommonLanguageId;

            if (textOffset + segment.Text.Length > args.Text.Length ||
                !args.Text.AsSpan(textOffset, segment.Text.Length).SequenceEqual(segment.Text.AsSpan()))
            {
                args.Segments = new List<PaperComponent.PaperTextSegment>
                {
                    CreateSegment(args.User, args.Text, SharedLanguageSystem.CommonLanguageId)
                };
                return;
            }

            normalized.Add(CreateSegment(args.User, segment.Text, language));
            textOffset += segment.Text.Length;
        }

        args.Segments = textOffset == args.Text.Length
            ? normalized
            : new List<PaperComponent.PaperTextSegment> { CreateSegment(args.User, args.Text, SharedLanguageSystem.CommonLanguageId) };
    }

    /// <summary>
    ///     Checks whether the paper already stores a segment that this one clearly
    ///     derives from: same language and overlapping text. The editor may attach
    ///     whitespace or split a multi-word segment while inserting new words, so
    ///     containment is enough to recognise previously written text.
    /// </summary>
    private static bool WasStoredOnPaper(Entity<PaperComponent> entity, PaperComponent.PaperTextSegment segment)
    {
        foreach (var stored in entity.Comp.LanguageSegments)
        {
            if (stored.Language != segment.Language)
                continue;

            if (segment.Text.Contains(stored.Text, StringComparison.Ordinal)
                || stored.Text.Contains(segment.Text, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private PaperComponent.PaperTextSegment CreateSegment(EntityUid writer, string text, string languageId)
    {
        if (!_proto.TryIndex<LanguagePrototype>(languageId, out var language))
            language = _proto.Index<LanguagePrototype>(SharedLanguageSystem.CommonLanguageId);

        // The common galactic tongue is understood by every sapient, so its text is never garbled.
        var obfuscated = language.ID == SharedLanguageSystem.CommonLanguageId
            ? text
            : _language.ObfuscateMessage(writer, text, language.Style, _random);

        return new PaperComponent.PaperTextSegment(text, language.ID, obfuscated);
    }
}
