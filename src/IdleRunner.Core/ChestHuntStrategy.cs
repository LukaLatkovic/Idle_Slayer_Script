namespace IdleRunner.Core;

public static class ChestHuntStrategy
{
    public static ChestHuntStep Decide(
        int openedChestIndex,
        ChestHuntStrategyState currentState,
        ChestHuntOutcome outcome,
        AutomationSettings settings)
    {
        if (openedChestIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(openedChestIndex));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (outcome == ChestHuntOutcome.HuntEnded)
        {
            return new ChestHuntStep(currentState, ChestHuntAction.Stop, "The Chest Hunt has ended.");
        }

        if (currentState == ChestHuntStrategyState.Normal
            || outcome == ChestHuntOutcome.LifeSaver)
        {
            return Continue(ChestHuntStrategyState.Normal, "The Life Saver strategy is complete.");
        }

        if (outcome == ChestHuntOutcome.DoubleReward)
        {
            return OpenLifeSaver("The 2x reward was found.");
        }

        if (settings.PerfectChestHunt
            && outcome == ChestHuntOutcome.Reward
            && ReachedPerfectStrategyLimit(openedChestIndex, currentState))
        {
            return OpenLifeSaver("The perfect-strategy search limit was reached.");
        }

        if (openedChestIndex == 0)
        {
            if (settings.NoReinforcedCrystalSaver)
            {
                return OpenLifeSaver("Reinforced Crystal Saver is unavailable.");
            }

            return outcome == ChestHuntOutcome.Mimic
                ? Continue(ChestHuntStrategyState.OneMimic, "The first chest was a Mimic.")
                : Continue(ChestHuntStrategyState.NoMimic, "The first chest was safe.");
        }

        if (openedChestIndex == 1)
        {
            if (!settings.PerfectChestHunt)
            {
                return OpenLifeSaver("The normal strategy opens the Life Saver after two chests.");
            }

            if (outcome == ChestHuntOutcome.Mimic)
            {
                ChestHuntStrategyState nextState = currentState == ChestHuntStrategyState.OneMimic
                    ? ChestHuntStrategyState.TwoMimics
                    : ChestHuntStrategyState.OneMimic;

                return Continue(nextState, "The second chest was a Mimic.");
            }

            return Continue(currentState, "The second chest was safe.");
        }

        return Continue(currentState, "Continue the current Chest Hunt strategy.");
    }

    private static bool ReachedPerfectStrategyLimit(
        int openedChestIndex,
        ChestHuntStrategyState currentState)
    {
        return currentState switch
        {
            ChestHuntStrategyState.NoMimic => openedChestIndex >= 13,
            ChestHuntStrategyState.OneMimic => openedChestIndex >= 15,
            ChestHuntStrategyState.TwoMimics => openedChestIndex >= 21,
            _ => false
        };
    }

    private static ChestHuntStep Continue(ChestHuntStrategyState state, string reason)
    {
        return new ChestHuntStep(state, ChestHuntAction.ContinueOpeningGrid, reason);
    }

    private static ChestHuntStep OpenLifeSaver(string reason)
    {
        return new ChestHuntStep(
            ChestHuntStrategyState.Normal,
            ChestHuntAction.OpenLifeSaver,
            reason);
    }
}
