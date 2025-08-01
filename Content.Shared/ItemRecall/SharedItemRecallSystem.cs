using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Robust.Shared.GameStates;
using Robust.Shared.Player;

namespace Content.Shared.ItemRecall;

/// <summary>
/// System for handling the ItemRecall ability for wizards.
/// </summary>
public abstract partial class SharedItemRecallSystem : EntitySystem
{
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    //[Dependency] private readonly SharedPvsOverrideSystem _pvs = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedPopupSystem _popups = default!;
    [Dependency] private readonly SharedProjectileSystem _proj = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemRecallComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ItemRecallComponent, OnItemRecallActionEvent>(OnItemRecallActionUse);

        SubscribeLocalEvent<RecallMarkerComponent, ComponentShutdown>(OnRecallMarkerShutdown);
    }

    private void OnMapInit(Entity<ItemRecallComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.InitialName = Name(ent);
        ent.Comp.InitialDescription = Description(ent);
    }

    private void OnItemRecallActionUse(Entity<ItemRecallComponent> ent, ref OnItemRecallActionEvent args)
    {
        if (ent.Comp.MarkedEntity == null)
        {
            if (!TryComp<HandsComponent>(args.Performer, out var hands))
                return;

            var markItem = _hands.GetActiveItem((args.Performer, hands));

            if (markItem == null)
            {
                _popups.PopupClient(Loc.GetString("item-recall-item-mark-empty"), args.Performer, args.Performer);
                return;
            }

            if (HasComp<RecallMarkerComponent>(markItem))
            {
                _popups.PopupClient(Loc.GetString("item-recall-item-already-marked", ("item", markItem)), args.Performer, args.Performer);
                return;
            }

            _popups.PopupClient(Loc.GetString("item-recall-item-marked", ("item", markItem.Value)), args.Performer, args.Performer);
            TryMarkItem(ent, markItem.Value);
            return;
        }

        RecallItem(ent.Comp.MarkedEntity.Value);
        args.Handled = true;
    }

    private void RecallItem(Entity<RecallMarkerComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp, false))
            return;

        if (_actions.GetAction(ent.Comp.MarkedByAction) is not {} action)
            return;

        if (action.Comp.AttachedEntity is not {} user)
            return;

        if (TryComp<EmbeddableProjectileComponent>(ent, out var projectile))
            _proj.EmbedDetach(ent, projectile, user);

        _popups.PopupPredicted(Loc.GetString("item-recall-item-summon-self", ("item", ent)),
                               Loc.GetString("item-recall-item-summon-others", ("item", ent), ("name", Identity.Entity(user, EntityManager))),
                               user, user);
        _popups.PopupPredictedCoordinates(Loc.GetString("item-recall-item-disappear", ("item", ent)), Transform(ent).Coordinates, user);

        _hands.TryForcePickupAnyHand(user, ent);
    }

    private void OnRecallMarkerShutdown(Entity<RecallMarkerComponent> ent, ref ComponentShutdown args)
    {
        TryUnmarkItem(ent);
    }

    private void TryMarkItem(Entity<ItemRecallComponent> ent, EntityUid item)
    {
        if (_actions.GetAction(ent.Owner) is not {} action)
            return;

        if (action.Comp.AttachedEntity is not {} user)
            return;

        AddToPvsOverride(item, user);

        ent.Comp.MarkedEntity = item;
        Dirty(ent);

        var marker = AddComp<RecallMarkerComponent>(item);
        marker.MarkedByAction = ent;
        Dirty(item, marker);

        UpdateActionAppearance((action, action, ent));
    }

    private void TryUnmarkItem(EntityUid item)
    {
        if (!TryComp<RecallMarkerComponent>(item, out var marker))
            return;

        if (_actions.GetAction(marker.MarkedByAction) is not {} action)
            return;

        if (TryComp<ItemRecallComponent>(action, out var itemRecall))
        {
            // For some reason client thinks the station grid owns the action on client and this doesn't work. It doesn't work in PopupEntity(mispredicts) and PopupPredicted either(doesnt show).
            // I don't have the heart to move this code to server because of this small thing.
            // This line will only do something once that is fixed.
            if (action.Comp.AttachedEntity is {} user)
            {
                _popups.PopupClient(Loc.GetString("item-recall-item-unmark", ("item", item)), user, user, PopupType.MediumCaution);
                RemoveFromPvsOverride(item, user);
            }

            itemRecall.MarkedEntity = null;
            UpdateActionAppearance((action, action, itemRecall));
            Dirty(action, itemRecall);
        }

        RemCompDeferred<RecallMarkerComponent>(item);
    }

    private void UpdateActionAppearance(Entity<ActionComponent, ItemRecallComponent> action)
    {
        if (action.Comp2.MarkedEntity is {} marked)
        {
            if (action.Comp2.WhileMarkedName is {} name)
                _metaData.SetEntityName(action, Loc.GetString(name, ("item", marked)));

            if (action.Comp2.WhileMarkedDescription is {} desc)
                _metaData.SetEntityDescription(action, Loc.GetString(desc, ("item", marked)));

            _actions.SetEntityIcon((action, action), marked);
        }
        else
        {
            if (action.Comp2.InitialName is {} name)
                _metaData.SetEntityName(action, name);
            if (action.Comp2.InitialDescription is {} desc)
                _metaData.SetEntityDescription(action, desc);
            _actions.SetEntityIcon((action, action), null);
        }
    }

    private void AddToPvsOverride(EntityUid uid, EntityUid user)
    {
        if (!_player.TryGetSessionByEntity(user, out var mindSession))
            return;

        //_pvs.AddSessionOverride(uid, mindSession);
    }

    private void RemoveFromPvsOverride(EntityUid uid, EntityUid user)
    {
        if (!_player.TryGetSessionByEntity(user, out var mindSession))
            return;

        //_pvs.RemoveSessionOverride(uid, mindSession);
    }
}
