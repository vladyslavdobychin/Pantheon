namespace Pantheon.Core.Cards;

public sealed class CardType
{
    public static readonly CardType Creature = new("Creature");
    public static readonly CardType Spell = new("Spell");
    public static readonly CardType Item = new("Item");

    public string Name { get; }

    private CardType(string name) => Name = name;

    public bool IsCreature => this == Creature;
    public bool IsSpell => this == Spell;
    public bool IsItem => this == Item;
}
