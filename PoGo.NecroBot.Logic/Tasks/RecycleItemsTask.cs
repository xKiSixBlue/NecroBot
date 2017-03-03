#region using directives

using System;
using System.Threading;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.Common;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.Logging;
using PoGo.NecroBot.Logic.Model;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Utils;
using POGOProtos.Inventory.Item;

#endregion

namespace PoGo.NecroBot.Logic.Tasks
{
    public class RecycleItemsTask
    {
        private static int _diff;
        private static Random rnd = new Random();

        public static async Task Execute(ISession session, CancellationToken cancellationToken)
        {

            cancellationToken.ThrowIfCancellationRequested();
            TinyIoC.TinyIoCContainer.Current.Resolve<MultiAccountManager>().ThrowIfSwitchAccountRequested();

            var currentTotalItems = session.Inventory.GetTotalItemCount();
            if ((session.Profile.PlayerData.MaxItemStorage * session.LogicSettings.RecycleInventoryAtUsagePercentage / 100.0f) > currentTotalItems)
                return;

            var currentAmountOfPokeballs = session.Inventory.GetItemAmountByType(ItemId.ItemPokeBall);
            var currentAmountOfGreatballs = session.Inventory.GetItemAmountByType(ItemId.ItemGreatBall);
            var currentAmountOfUltraballs = session.Inventory.GetItemAmountByType(ItemId.ItemUltraBall);
            var currentAmountOfMasterballs = session.Inventory.GetItemAmountByType(ItemId.ItemMasterBall);

            if (session.LogicSettings.DetailedCountsBeforeRecycling)
                Logger.Write(session.Translation.GetTranslation(TranslationString.CurrentPokeballInv,
                    currentAmountOfPokeballs, currentAmountOfGreatballs, currentAmountOfUltraballs,
                    currentAmountOfMasterballs));

            var currentPotions = session.Inventory.GetItemAmountByType(ItemId.ItemPotion);
            var currentSuperPotions = session.Inventory.GetItemAmountByType(ItemId.ItemSuperPotion);
            var currentHyperPotions = session.Inventory.GetItemAmountByType(ItemId.ItemHyperPotion);
            var currentMaxPotions = session.Inventory.GetItemAmountByType(ItemId.ItemMaxPotion);

            var currentAmountOfPotions = currentPotions + currentSuperPotions + currentHyperPotions + currentMaxPotions;

            if (session.LogicSettings.DetailedCountsBeforeRecycling)
                Logger.Write(session.Translation.GetTranslation(TranslationString.CurrentPotionInv,
                    currentPotions, currentSuperPotions, currentHyperPotions, currentMaxPotions));

            var currentRevives = session.Inventory.GetItemAmountByType(ItemId.ItemRevive);
            var currentMaxRevives = session.Inventory.GetItemAmountByType(ItemId.ItemMaxRevive);

            var currentAmountOfRevives = currentRevives + currentMaxRevives;

            if (session.LogicSettings.DetailedCountsBeforeRecycling)
                Logger.Write(session.Translation.GetTranslation(TranslationString.CurrentReviveInv,
                    currentRevives, currentMaxRevives));

            var currentAmountOfBerries = session.Inventory.GetItemAmountByType(ItemId.ItemRazzBerry) +
                                         session.Inventory.GetItemAmountByType(ItemId.ItemBlukBerry) +
                                         session.Inventory.GetItemAmountByType(ItemId.ItemNanabBerry) +
                                         session.Inventory.GetItemAmountByType(ItemId.ItemWeparBerry) +
                                         session.Inventory.GetItemAmountByType(ItemId.ItemPinapBerry);
            var currentAmountOfIncense = session.Inventory.GetItemAmountByType(ItemId.ItemIncenseOrdinary) +
                                         session.Inventory.GetItemAmountByType(ItemId.ItemIncenseSpicy) +
                                         session.Inventory.GetItemAmountByType(ItemId.ItemIncenseCool) +
                                         session.Inventory.GetItemAmountByType(ItemId.ItemIncenseFloral);
            var currentAmountOfLuckyEggs = session.Inventory.GetItemAmountByType(ItemId.ItemLuckyEgg);
            var currentAmountOfLures = session.Inventory.GetItemAmountByType(ItemId.ItemTroyDisk);

            if (session.LogicSettings.DetailedCountsBeforeRecycling)
                Logger.Write(session.Translation.GetTranslation(TranslationString.CurrentMiscItemInv,
                    currentAmountOfBerries, currentAmountOfIncense, currentAmountOfLuckyEggs, currentAmountOfLures));

            if (!session.LogicSettings.VerboseRecycling)
                Logger.Write(session.Translation.GetTranslation(TranslationString.RecyclingQuietly), LogLevel.Recycling);

            await OptimizedRecycleBalls(session, cancellationToken);
            await OptimizedRecyclePotions(session, cancellationToken);
            await OptimizedRecycleRevives(session, cancellationToken);
            await OptimizedRecycleBerries(session, cancellationToken);

            //await session.Inventory.RefreshCachedInventory();
            currentTotalItems = session.Inventory.GetTotalItemCount();
            if ((session.Profile.PlayerData.MaxItemStorage * session.LogicSettings.RecycleInventoryAtUsagePercentage / 100.0f) > currentTotalItems)
                return;

            var items = session.Inventory.GetItemsToRecycle(session);

            foreach (var item in items)
            {
                if (item.Count <= 1 || 
                    (session.SaveBallForByPassCatchFlee && 
                        (item.ItemId == ItemId.ItemPokeBall || 
                        item.ItemId == ItemId.ItemGreatBall || 
                        item.ItemId == ItemId.ItemUltraBall))

                    ) continue;
                
                cancellationToken.ThrowIfCancellationRequested();
                TinyIoC.TinyIoCContainer.Current.Resolve<MultiAccountManager>().ThrowIfSwitchAccountRequested();
                await session.Client.Inventory.RecycleItem(item.ItemId, item.Count);
                await session.Inventory.UpdateInventoryItem(item.ItemId);

                if (session.LogicSettings.VerboseRecycling)
                    session.EventDispatcher.Send(new ItemRecycledEvent { Id = item.ItemId, Count = item.Count });

                DelayingUtils.Delay(session.LogicSettings.RecycleActionDelay, 500);
            }
            //await session.Inventory.RefreshCachedInventory();
        }

        private static async Task RecycleItems(ISession session, CancellationToken cancellationToken, int itemCount, ItemId item, int maxItemToKeep = 1000)
        {
            int itemsToRecycle = 0;
            int itemsToKeep = itemCount - _diff;
            if (itemsToKeep < 0)
                itemsToKeep = 0;

            if (maxItemToKeep > 0)
            {
                itemsToKeep = Math.Min(itemsToKeep, maxItemToKeep);
            }
            itemsToRecycle = itemCount - itemsToKeep;
            if (itemsToRecycle > 0)
            {
                _diff -= itemsToRecycle;
                cancellationToken.ThrowIfCancellationRequested();
                TinyIoC.TinyIoCContainer.Current.Resolve<MultiAccountManager>().ThrowIfSwitchAccountRequested();
                await session.Client.Inventory.RecycleItem(item, itemsToRecycle);
                await session.Inventory.UpdateInventoryItem(item);
                if (session.LogicSettings.VerboseRecycling)
                    session.EventDispatcher.Send(new ItemRecycledEvent { Id = item, Count = itemsToRecycle });

                DelayingUtils.Delay(session.LogicSettings.RecycleActionDelay, 500);
            }
        }

        private static async Task OptimizedRecycleBalls(ISession session, CancellationToken cancellationToken)
        {
            var pokeBallsCount = session.Inventory.GetItemAmountByType(ItemId.ItemPokeBall);
            var greatBallsCount = session.Inventory.GetItemAmountByType(ItemId.ItemGreatBall);
            var ultraBallsCount = session.Inventory.GetItemAmountByType(ItemId.ItemUltraBall);
            var masterBallsCount = session.Inventory.GetItemAmountByType(ItemId.ItemMasterBall);

            int totalBallsCount = pokeBallsCount + greatBallsCount + ultraBallsCount + masterBallsCount;

            if (session.SaveBallForByPassCatchFlee) return;

            int random = rnd.Next(-1 * session.LogicSettings.RandomRecycleValue, session.LogicSettings.RandomRecycleValue + 1);

            int totalPokeballsToKeep;

            if (session.LogicSettings.UseRecyclePercentsInsteadOfTotals)
            {
                totalPokeballsToKeep = (int)Math.Floor(session.LogicSettings.PercentOfInventoryPokeballsToKeep / 100.0 * session.Profile.PlayerData.MaxItemStorage);
            }
            else
            {
                totalPokeballsToKeep = session.LogicSettings.TotalAmountOfPokeballsToKeep;
            }

            if (totalBallsCount > totalPokeballsToKeep)
            {
                if (session.LogicSettings.RandomizeRecycle)
                {
                    _diff = totalBallsCount - totalPokeballsToKeep + random;
                }
                else
                {
                    _diff = totalBallsCount - totalPokeballsToKeep;
                }

                if (_diff > 0)
                {
                    await RecycleItems(session, cancellationToken, pokeBallsCount, ItemId.ItemPokeBall);
                }
                if (_diff > 0)
                {
                    await RecycleItems(session, cancellationToken, greatBallsCount, ItemId.ItemGreatBall);
                }
                if (_diff > 0)
                {
                    await RecycleItems(session, cancellationToken, ultraBallsCount, ItemId.ItemUltraBall);
                }
                if (_diff > 0)
                {
                    await RecycleItems(session, cancellationToken, masterBallsCount, ItemId.ItemMasterBall);
                }
            }
        }

        private static async Task OptimizedRecyclePotions(ISession session, CancellationToken cancellationToken)
        {
            var potionCount = session.Inventory.GetItemAmountByType(ItemId.ItemPotion);
            var superPotionCount = session.Inventory.GetItemAmountByType(ItemId.ItemSuperPotion);
            var hyperPotionsCount = session.Inventory.GetItemAmountByType(ItemId.ItemHyperPotion);
            var maxPotionCount = session.Inventory.GetItemAmountByType(ItemId.ItemMaxPotion);

            int totalPotionsCount = potionCount + superPotionCount + hyperPotionsCount + maxPotionCount;
            int random = rnd.Next(-1 * session.LogicSettings.RandomRecycleValue, session.LogicSettings.RandomRecycleValue + 1);

            int totalPotionsToKeep;
            if (session.LogicSettings.UseRecyclePercentsInsteadOfTotals)
            {
                totalPotionsToKeep = (int)Math.Floor(session.LogicSettings.PercentOfInventoryPotionsToKeep / 100.0 * session.Profile.PlayerData.MaxItemStorage);
            }
            else
            {
                totalPotionsToKeep = session.LogicSettings.TotalAmountOfPotionsToKeep;
            }

            if (totalPotionsCount > totalPotionsToKeep)
            {
                if (session.LogicSettings.RandomizeRecycle)
                {
                    _diff = totalPotionsCount - totalPotionsToKeep + random;
                }
                else
                {
                    _diff = totalPotionsCount - totalPotionsToKeep;
                }

                if (_diff > 0)
                {
                    await RecycleItems(session, cancellationToken, potionCount, ItemId.ItemPotion);
                }

                if (_diff > 0)
                {
                    await RecycleItems(session, cancellationToken, superPotionCount, ItemId.ItemSuperPotion);
                }

                if (_diff > 0)
                {
                    await RecycleItems(session, cancellationToken, hyperPotionsCount, ItemId.ItemHyperPotion);
                }

                if (_diff > 0)
                {
                    await RecycleItems(session, cancellationToken, maxPotionCount, ItemId.ItemMaxPotion);
                }
            }
        }

        private static async Task OptimizedRecycleRevives(ISession session, CancellationToken cancellationToken)
        {
            var reviveCount = session.Inventory.GetItemAmountByType(ItemId.ItemRevive);
            var maxReviveCount = session.Inventory.GetItemAmountByType(ItemId.ItemMaxRevive);

            int totalRevivesCount = reviveCount + maxReviveCount;
            int random = rnd.Next(-1 * session.LogicSettings.RandomRecycleValue, session.LogicSettings.RandomRecycleValue + 1);

            int totalRevivesToKeep;
            if (session.LogicSettings.UseRecyclePercentsInsteadOfTotals)
            {
                totalRevivesToKeep = (int)Math.Floor(session.LogicSettings.PercentOfInventoryRevivesToKeep / 100.0 * session.Profile.PlayerData.MaxItemStorage);
            }
            else
            {
                totalRevivesToKeep = session.LogicSettings.TotalAmountOfRevivesToKeep;
            }

            if (totalRevivesCount > totalRevivesToKeep)
            {
                if (session.LogicSettings.RandomizeRecycle)
                {
                    _diff = totalRevivesCount - totalRevivesToKeep + random;
                }
                else
                {
                    _diff = totalRevivesCount - totalRevivesToKeep;
                }
                if (_diff > 0)
                {
                    await RecycleItems(session, cancellationToken, reviveCount, ItemId.ItemRevive);
                }

                if (_diff > 0)
                {
                    await RecycleItems(session, cancellationToken, maxReviveCount, ItemId.ItemMaxRevive);
                }
            }
        }

        private static async Task OptimizedRecycleBerries(ISession session, CancellationToken cancellationToken)
        {
            var razz = session.Inventory.GetItemAmountByType(ItemId.ItemRazzBerry);
            var bluk = session.Inventory.GetItemAmountByType(ItemId.ItemBlukBerry);
            var nanab = session.Inventory.GetItemAmountByType(ItemId.ItemNanabBerry);
            var pinap = session.Inventory.GetItemAmountByType(ItemId.ItemPinapBerry);
            var wepar = session.Inventory.GetItemAmountByType(ItemId.ItemWeparBerry);

            int totalBerryCount = razz + bluk + nanab + pinap + wepar;
            int random = rnd.Next(-1 * session.LogicSettings.RandomRecycleValue, session.LogicSettings.RandomRecycleValue + 1);

            int totalBerriesToKeep;
            if (session.LogicSettings.UseRecyclePercentsInsteadOfTotals)
            {
                totalBerriesToKeep = (int)Math.Floor(session.LogicSettings.PercentOfInventoryBerriesToKeep / 100.0 * session.Profile.PlayerData.MaxItemStorage);
            }
            else
            {
                totalBerriesToKeep = session.LogicSettings.TotalAmountOfBerriesToKeep;
            }

            if (totalBerryCount > totalBerriesToKeep)
            {
                if (session.LogicSettings.RandomizeRecycle)
                {
                    _diff = totalBerryCount - totalBerriesToKeep + random;
                }
                else
                {
                    _diff = totalBerryCount - totalBerriesToKeep;
                }

                if (_diff > 0)
                {
                    await RecycleItems(session, cancellationToken, razz, ItemId.ItemRazzBerry);
                }

                if (_diff > 0)
                {
                    await RecycleItems(session, cancellationToken, bluk, ItemId.ItemBlukBerry);
                }

                if (_diff > 0)
                {
                    await RecycleItems(session, cancellationToken, nanab, ItemId.ItemNanabBerry);
                }

                if (_diff > 0)
                {
                    await RecycleItems(session, cancellationToken, pinap, ItemId.ItemPinapBerry);
                }

                if (_diff > 0)
                {
                    await RecycleItems(session, cancellationToken, wepar, ItemId.ItemWeparBerry);
                }
            }
        }

        public static async Task DropItem(ISession session, ItemId item, int count)
        {
            using (var blocker = new BlockableScope(session, BotActions.RecycleItem))
            {
                if (!await blocker.WaitToRun()) return;

                if (count > 0)
                {
                    await session.Client.Inventory.RecycleItem(item, count);
                    await session.Inventory.UpdateInventoryItem(item);

                    if (session.LogicSettings.VerboseRecycling)
                        session.EventDispatcher.Send(new ItemRecycledEvent { Id = item, Count = count });

                    DelayingUtils.Delay(session.LogicSettings.RecycleActionDelay, 500);
                }
            }
        }
    }
}