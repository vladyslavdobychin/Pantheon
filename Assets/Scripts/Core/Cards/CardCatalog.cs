namespace Pantheon.Core.Cards
{
    public static class CardCatalog
    {
        public static readonly CardDefinition Hoplite =
            new("Hoplite", new CardPrice(1), CardType.Creature, attack: 1, health: 2);
    }
}
