﻿using Newtonsoft.Json;
using Oasys.Common.Enums.GameEnums;
using Oasys.Common.GameObject;
using Oasys.Common.GameObject.ObjectClass;
using Oasys.Common.Menu;
using Oasys.Common.Menu.ItemComponents;
using Oasys.SDK;
using Oasys.SDK.Menu;
using Oasys.SDK.SpellCasting;
using SixAIO.Enums;
using SixAIO.Models;
using SixAIO.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SixAIO.Champions
{
    internal class Tristana : Champion
    {
        private static TargetSelection _targetSelection;

        private static float GetRDamage(GameObjectBase target)
        {
            return DamageCalculator.GetMagicResistMod(UnitManager.MyChampion, target) *
                   (UnitManager.MyChampion.UnitStats.TotalAbilityPower + 200 + 100 * UnitManager.MyChampion.GetSpellBook().GetSpellClass(SpellSlot.R).Level);
        }

        public Tristana()
        {
            SpellQ = new Spell(CastSlot.Q, SpellSlot.Q)
            {
                IsEnabled = () => UseQ,
                ShouldCast = (mode, target, spellClass, damage) => GetETarget(UnitManager.EnemyChampions) is not null
            };
            SpellE = new Spell(CastSlot.E, SpellSlot.E)
            {
                IsTargetted = () => true,
                IsEnabled = () => UseE,
                TargetSelect = (mode) => UseTargetselector
                ? Orbwalker.TargetHero
                : GetPrioritizationTarget(),
                ShouldCast = (mode, target, spellClass, damage) => target is not null && target.Distance <= 517 + 8 * UnitManager.MyChampion.Level
            };
            SpellR = new Spell(CastSlot.R, SpellSlot.R)
            {
                IsTargetted = () => true,
                Damage = (target, spellClass) =>
                            target != null
                            ? GetRDamage(target)
                            : 0,
                IsEnabled = () => UseR,
                TargetSelect = (mode) => TargetSelectR()
            };
        }

        private Hero TargetSelectR()
        {
            var targets = UnitManager.EnemyChampions.Where(x => !x.IsTargetDummy).Where(x => x.Distance <= UnitManager.MyChampion.TrueAttackRange &&
                                                                TargetSelector.IsAttackable(x) &&
                                                                !TargetSelector.IsInvulnerable(x, Oasys.Common.Logic.DamageType.Magical, false) &&
                                                                RSettings.GetItem<Switch>("R - " + x.ModelName).IsOn)
                                                    .OrderBy(x => x.Health);

            var target = targets.FirstOrDefault(x => DamageCalculator.GetTargetHealthAfterBasicAttack(UnitManager.MyChampion, x) + x.NeutralShield + x.MagicalShield + 100 < GetRDamage(x));
            if (target != null)
            {
                return target;
            }
            if (UsePushAway)
            {
                return PushAwayModeSelected switch
                {
                    PushAwayMode.Melee => targets.FirstOrDefault(x => x.CombatType == CombatTypes.Melee && x.Distance < PushAwayRange),
                    PushAwayMode.LowerThanMyRange => targets.FirstOrDefault(x => x.AttackRange < UnitManager.MyChampion.AttackRange && x.Distance < PushAwayRange),
                    PushAwayMode.DashNearMe => targets.FirstOrDefault(x => x.AIManager.IsDashing && UnitManager.MyChampion.DistanceTo(x.AIManager.NavEndPosition) < 300 && x.Distance < PushAwayRange),
                    PushAwayMode.Everything => targets.FirstOrDefault(x => x.Distance < PushAwayRange),
                    _ => null,
                };
            }

            return null;
        }

        internal override void OnCoreMainInput()
        {
            Orbwalker.SelectedTarget = GetETarget(UnitManager.EnemyChampions);
            if (SpellQ.ExecuteCastSpell() || SpellE.ExecuteCastSpell() || SpellR.ExecuteCastSpell())
            {
                return;
            }
        }

        internal override void OnCoreLaneClearInput()
        {
            Orbwalker.SelectedTarget = GetETarget(UnitManager.Enemies);
            SpellQ.ExecuteCastSpell();
        }

        private static GameObjectBase GetETarget<T>(List<T> enemies) where T : GameObjectBase
        {
            return enemies.FirstOrDefault(x => TargetSelector.IsAttackable(x) && x.Distance <= UnitManager.MyChampion.TrueAttackRange && !x.IsObject(ObjectTypeFlag.BuildingProps) && x.BuffManager.HasActiveBuff("tristanaechargesound"));
        }

        private bool UsePushAway
        {
            get => RSettings.GetItem<Switch>("Use Push Away").IsOn;
            set => RSettings.GetItem<Switch>("Use Push Away").IsOn = value;
        }

        private int PushAwayRange
        {
            get => RSettings.GetItem<Counter>("Push Away Range").Value;
            set => RSettings.GetItem<Counter>("Push Away Range").Value = value;
        }

        private PushAwayMode PushAwayModeSelected
        {
            get => (PushAwayMode)Enum.Parse(typeof(PushAwayMode), RSettings.GetItem<ModeDisplay>("Push Away Mode").SelectedModeName);
            set => RSettings.GetItem<ModeDisplay>("Push Away Mode").SelectedModeName = value.ToString();
        }

        internal void LoadTargetPrioValues()
        {
            try
            {
                using var stream = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(assembly => assembly.GetName().Name == "Oasys.Core").GetManifestResourceStream("Oasys.Core.Dependencies.TargetSelection.json");
                using var reader = new StreamReader(stream);
                var jsonText = reader.ReadToEnd();

                _targetSelection = JsonConvert.DeserializeObject<TargetSelection>(jsonText);
                var enemies = UnitManager.EnemyChampions.Where(x => !x.IsTargetDummy);

                InitializeSettings(_targetSelection.TargetPrioritizations.Where(x => enemies.Any(e => e.ModelName.Equals(x.Champion, StringComparison.OrdinalIgnoreCase))));
            }
            catch (Exception)
            {
            }
        }

        internal void InitializeSettings(IEnumerable<TargetPrioritization> targetPrioritizations)
        {
            try
            {
                if (targetPrioritizations.Any())
                {
                    ESettings.AddItem(new InfoDisplay() { Title = "-E target prio-" });
                }
                foreach (var targetPrioritization in targetPrioritizations)
                {
                    ESettings.AddItem(new Counter() { Title = targetPrioritization.Champion, MinValue = 0, MaxValue = 5, Value = targetPrioritization.Prioritization, ValueFrequency = 1 });
                }
            }
            catch (Exception)
            {
            }
        }

        private GameObjectBase GetPrioritizationTarget()
        {
            try
            {
                GameObjectBase tempTarget = null;
                var tempPrio = 0;

                foreach (var hero in UnitManager.EnemyChampions.Where(x => x.Distance <= ETargetRange && TargetSelector.IsAttackable(x)))
                {
                    try
                    {
                        var targetPrio = ESettings.GetItem<Counter>(x => x.Title == hero.ModelName)?.Value ?? 1;
                        if (targetPrio > tempPrio)
                        {
                            tempPrio = targetPrio;
                            tempTarget = hero;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                return tempTarget;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private bool UseTargetselector
        {
            get => ESettings.GetItem<Switch>("Use Targetselector").IsOn;
            set => ESettings.GetItem<Switch>("Use Targetselector").IsOn = value;
        }

        private int ETargetRange
        {
            get => ESettings.GetItem<Counter>("E target range").Value;
            set => ESettings.GetItem<Counter>("E target range").Value = value;
        }


        internal override void InitializeMenu()
        {
            MenuManager.AddTab(new Tab($"SIXAIO - {nameof(Tristana)}"));

            MenuTab.AddGroup(new Group("Q Settings"));
            MenuTab.AddGroup(new Group("E Settings"));
            MenuTab.AddGroup(new Group("R Settings"));

            QSettings.AddItem(new Switch() { Title = "Use Q", IsOn = true });

            ESettings.AddItem(new Switch() { Title = "Use E", IsOn = true });
            ESettings.AddItem(new Switch() { Title = "Use Targetselector", IsOn = false });
            ESettings.AddItem(new Counter() { Title = "E target range", Value = 1000, MinValue = 0, MaxValue = 2000, ValueFrequency = 50 });
            LoadTargetPrioValues();

            RSettings.AddItem(new Switch() { Title = "Use R", IsOn = true });
            foreach (var enemy in UnitManager.EnemyChampions.Where(x => !x.IsTargetDummy))
            {
                RSettings.AddItem(new Switch() { Title = "R - " + enemy.ModelName, IsOn = true });
            }

            RSettings.AddItem(new Switch() { Title = "Use Push Away", IsOn = false });
            RSettings.AddItem(new Counter() { Title = "Push Away Range", MinValue = 50, MaxValue = 500, Value = 150, ValueFrequency = 25 });
            RSettings.AddItem(new ModeDisplay() { Title = "Push Away Mode", ModeNames = PushAwayHelper.ConstructPushAwayModeTable(), SelectedModeName = "Melee" });

        }
    }
}
