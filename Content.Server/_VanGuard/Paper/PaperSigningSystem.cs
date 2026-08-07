using Content.Server.Access.Systems;
using Content.Server.Popups;
using Content.Shared.Paper;
using Content.Shared._VanGuard.Paper;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Server.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._VanGuard.Paper;

public sealed partial class PaperSigningSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IdCardSystem _idCard = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private TagSystem _tags = default!;

    private static readonly ProtoId<TagPrototype> WritingTool = "Write";

    private const string SignatureSpriteState = "sign";
    private const string SignatureTypeface = "/Fonts/_VanGuard/goodvibescyr.ttf";

    public override void Initialize()
    {
        SubscribeLocalEvent<PaperComponent, GetVerbsEvent<AlternativeVerb>>(OnAlternativeVerbRequested);
    }

    private void OnAlternativeVerbRequested(Entity<PaperComponent> document, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using is not { } pen || !_tags.HasTag(pen, WritingTool))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => TrySign(document, user, pen),
            Text = Loc.GetString("paper-signing-verb"),
            DoContactInteraction = true,
            Priority = 10
        });
    }

    public bool TrySign(Entity<PaperComponent> document, EntityUid signer, EntityUid pen)
    {
        var attempt = new PaperSigningAttemptEvent(document.Owner, signer);
        RaiseLocalEvent(pen, ref attempt);
        if (attempt.Denied)
            return false;

        var signatureName = GetSignatureName(signer);
        if (HasSignature(document.Comp, signatureName) || !ApplySignature(document, signatureName, pen))
        {
            NotifySigningFailure(signer, document.Owner);
            return false;
        }

        AnnounceSignature(signer, document.Owner);
        _audio.PlayPvs(document.Comp.Sound, signer);
        _paper.UpdateUserInterface(document);

        return true;
    }

    private bool ApplySignature(Entity<PaperComponent> document, string signatureName, EntityUid pen)
    {
        var ink = SignatureInkColor.Black.ToColor();
        if (TryComp<SignatureComponent>(pen, out var signatureComp))
            ink = signatureComp.Color.ToColor();

        var mark = new StampDisplayInfo
        {
            StampedName = signatureName,
            StampedColor = ink,
            Kind = PaperMarkType.Handwritten,
            Typeface = SignatureTypeface
        };

        return _paper.TryStamp(document, mark, SignatureSpriteState, ink);
    }

    private bool HasSignature(PaperComponent paper, string signatureName)
    {
        foreach (var mark in paper.StampedBy)
        {
            if (mark.Kind == PaperMarkType.Handwritten && mark.StampedName == signatureName)
                return true;
        }

        return false;
    }

    private string GetSignatureName(EntityUid entity)
    {
        if (_idCard.TryFindIdCard(entity, out var idCard) && !string.IsNullOrWhiteSpace(idCard.Comp.FullName))
            return idCard.Comp.FullName;

        return Name(entity);
    }

    private void AnnounceSignature(EntityUid signer, EntityUid document)
    {
        var toOthers = Loc.GetString("paper-signing-other", ("user", signer), ("target", document));
        _popup.PopupEntity(toOthers, signer, Filter.PvsExcept(signer, entityManager: EntityManager), true);

        var toSelf = Loc.GetString("paper-signing-self", ("target", document));
        _popup.PopupEntity(toSelf, signer, signer);
    }

    private void NotifySigningFailure(EntityUid signer, EntityUid document)
    {
        var message = Loc.GetString("paper-signing-failure", ("target", document));
        _popup.PopupEntity(message, signer, signer, PopupType.SmallCaution);
    }
}
