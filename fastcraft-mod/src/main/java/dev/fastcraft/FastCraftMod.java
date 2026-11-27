package dev.fastcraft;

import java.util.function.UnaryOperator;

import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.biome.v1.BiomeModifications;
import net.fabricmc.fabric.api.biome.v1.BiomeSelectors;
import net.fabricmc.fabric.api.itemgroup.v1.CreativeModeTabEvents;
import net.minecraft.world.item.CreativeModeTabs;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.tags.BlockTags;
import net.minecraft.tags.TagKey;
import net.minecraft.world.item.BlockItem;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ToolMaterial;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.SoundType;
import net.minecraft.world.level.block.state.BlockBehaviour;
import net.minecraft.world.level.levelgen.GenerationStep;
import net.minecraft.world.level.levelgen.placement.PlacedFeature;
import net.minecraft.world.level.material.MapColor;

/**
 * The FastCraft amber tier. 26.3 is unobfuscated, so these are the game's own names:
 * Identifier (renamed from ResourceLocation in 25w45a), a block and items registered with a
 * ResourceKey, every item given its id through Item.Properties#setId, and five tools built
 * with the properties' own sword/pickaxe/axe/shovel/hoe factories.
 *
 * The repair item is a tag, because that is what ToolMaterial's last parameter is in 26.3.
 */
public class FastCraftMod implements ModInitializer
{
    public static final String ID = "fastcraft";

    private static final TagKey<Item> REPAIRS_AMBER =
        TagKey.create(BuiltInRegistries.ITEM.key(), id("repairs_amber"));

    // Between copper (190 uses, speed 5, +1 damage) and iron (250, 6, +2).
    public static final ToolMaterial AMBER = new ToolMaterial(
        BlockTags.INCORRECT_FOR_IRON_TOOL, 220, 5.5F, 1.5F, 12, REPAIRS_AMBER);

    public static final Item RAW_AMBER = item("raw_amber", new Item.Properties());
    public static final Item AMBER_INGOT = item("amber_ingot", new Item.Properties());

    private static final ResourceKey<Block> AMBER_ORE_KEY =
        ResourceKey.create(Registries.BLOCK, id("amber_ore"));

    private static final Block AMBER_ORE = new Block(BlockBehaviour.Properties.of()
        .setId(AMBER_ORE_KEY)
        .mapColor(MapColor.COLOR_ORANGE)
        .requiresCorrectToolForDrops()
        .strength(3.0F, 3.0F)
        .sound(SoundType.STONE));

    public static final Item AMBER_ORE_ITEM = blockItem("amber_ore", AMBER_ORE);

    public static final Item AMBER_SWORD = tool("amber_sword", properties -> properties.sword(AMBER, 3.0F, -2.4F));
    public static final Item AMBER_PICKAXE = tool("amber_pickaxe", properties -> properties.pickaxe(AMBER, 1.0F, -2.8F));
    public static final Item AMBER_AXE = tool("amber_axe", properties -> properties.axe(AMBER, 5.5F, -3.0F));
    public static final Item AMBER_SHOVEL = tool("amber_shovel", properties -> properties.shovel(AMBER, 1.5F, -3.0F));
    public static final Item AMBER_HOE = tool("amber_hoe", properties -> properties.hoe(AMBER, -2.0F, 0.0F));

    private static final ResourceKey<PlacedFeature> AMBER_ORE_PLACED =
        ResourceKey.create(Registries.PLACED_FEATURE, id("amber_ore"));

    @Override
    public void onInitialize()
    {
        Registry.register(BuiltInRegistries.BLOCK, AMBER_ORE_KEY, AMBER_ORE);

        BiomeModifications.addFeature(BiomeSelectors.foundInOverworld(),
            GenerationStep.Decoration.UNDERGROUND_ORES, AMBER_ORE_PLACED);

        // Findable in the creative inventory, beside the material each thing belongs to.
        CreativeModeTabEvents.modifyOutputEvent(CreativeModeTabs.INGREDIENTS).register(output ->
        {
            output.accept(RAW_AMBER);
            output.accept(AMBER_INGOT);
        });
        CreativeModeTabEvents.modifyOutputEvent(CreativeModeTabs.NATURAL_BLOCKS).register(output ->
            output.accept(AMBER_ORE_ITEM));
        CreativeModeTabEvents.modifyOutputEvent(CreativeModeTabs.TOOLS).register(output ->
        {
            output.accept(AMBER_SWORD);
            output.accept(AMBER_PICKAXE);
            output.accept(AMBER_AXE);
            output.accept(AMBER_SHOVEL);
            output.accept(AMBER_HOE);
        });
    }

    private static Item item(String name, Item.Properties properties)
    {
        var key = ResourceKey.create(Registries.ITEM, id(name));
        var item = new Item(properties.setId(key));
        Registry.register(BuiltInRegistries.ITEM, key, item);
        return item;
    }

    private static Item blockItem(String name, Block block)
    {
        var key = ResourceKey.create(Registries.ITEM, id(name));
        var item = new BlockItem(block, new Item.Properties().setId(key));
        Registry.register(BuiltInRegistries.ITEM, key, item);
        return item;
    }

    private static Item tool(String name, UnaryOperator<Item.Properties> shape)
    {
        return item(name, shape.apply(new Item.Properties()));
    }

    public static Identifier id(String path)
    {
        return Identifier.fromNamespaceAndPath(ID, path);
    }
}
