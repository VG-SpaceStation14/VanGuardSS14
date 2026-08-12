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
            // Malformed payload: refuse to persist anything instead of storing the
            // text as a plain common-tongue segment.
            args.Cancelled = true;
            return;
        }

        // Determine which submitted segments already exist on the paper (written by
        // an earlier author). Those are never downgraded: a second author may not
        // speak the language, but must not be able to make previously written text
        // readable for everyone.
        var preExisting = FindPreExistingSegments(entity, args.Segments);

        var normalized = new List<PaperComponent.PaperTextSegment>();
        var textOffset = 0;

        for (var index = 0; index < args.Segments.Count; index++)
        {
            var segment = args.Segments[index];
            if (string.IsNullOrEmpty(segment.Text))
                continue;

            var language = segment.Language;
            // Gestural languages have no written form and cannot be stored on paper.
            var writable = _proto.TryIndex<LanguagePrototype>(language, out var languageProto) && languageProto.Written;
            if ((!writable || !_language.CanSpeak(args.User, language)) && !preExisting.Contains(index))
                language = SharedLanguageSystem.CommonLanguageId;

            if (textOffset + segment.Text.Length > args.Text.Length ||
                !args.Text.AsSpan(textOffset, segment.Text.Length).SequenceEqual(segment.Text.AsSpan()))
            {
                args.Cancelled = true;
                return;
            }

            normalized.Add(CreateSegment(args.User, segment.Text, language));
            textOffset += segment.Text.Length;
        }

        if (textOffset != args.Text.Length)
        {
            args.Cancelled = true;
            return;
        }

        args.Segments = normalized;
    }

    /// <summary>
    ///     Finds the submitted segments that reproduce text already stored on the
    ///     paper. A stored segment matches when it can be reconstructed exactly
    ///     (character for character) from a run of submitted segments with the same
    ///     language; segments in other languages may be interleaved when a new word
    ///     was inserted into the middle of a stored multi-word segment. Every
    ///     submitted segment is consumed at most once, so a single stored entry can
    ///     never be reused to launder multiple new submissions.
    /// </summary>
    private static HashSet<int> FindPreExistingSegments(
        Entity<PaperComponent> entity,
        List<PaperComponent.PaperTextSegment> submitted)
    {
        var preExisting = new HashSet<int>();
        var consumed = new HashSet<int>();

        foreach (var stored in entity.Comp.LanguageSegments)
        {
            if (string.IsNullOrEmpty(stored.Text))
                continue;

            var run = new List<int>();
            var length = 0;

            for (var index = 0; index < submitted.Count; index++)
            {
                if (consumed.Contains(index) || submitted[index].Language != stored.Language)
                    continue;

                run.Add(index);
                length += submitted[index].Text.Length;

                if (length == stored.Text.Length)
                {
                    var sb = new StringBuilder();
                    foreach (var runIndex in run)
                        sb.Append(submitted[runIndex].Text);

                    if (sb.ToString() == stored.Text)
                    {
                        foreach (var runIndex in run)
                        {
                            consumed.Add(runIndex);
                            preExisting.Add(runIndex);
                        }
                    }

                    break;
                }

                if (length > stored.Text.Length)
                    break;
            }
        }

        return preExisting;
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
