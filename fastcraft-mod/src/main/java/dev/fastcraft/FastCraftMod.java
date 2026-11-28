package dev.fastcraft;

import java.util.function.UnaryOperator;

import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.biome.v1.BiomeModifications;
import net.fabricmc.fabric.api.biome.v1.BiomeSelectors;
import net.fabricmc.fabric.api.creativetab.v1.CreativeModeTabEvents;
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
 * The FastCraft ruby tier. 26.3 is unobfuscated, so these are the game's own names:
 * Identifier (renamed from ResourceLocation in 25w45a), a block and items registered with a
 * ResourceKey, every item given its id through Item.Properties#setId, and five tools built
 * with the properties' own sword/pickaxe/axe/shovel/hoe factories.
 *
 * The repair item is a tag, because that is what ToolMaterial's last parameter is in 26.3.
 */
public class FastCraftMod implements ModInitializer
{
    public static final String ID = "fastcraft";

    private static final TagKey<Item> REPAIRS_RUBY =
        TagKey.create(BuiltInRegistries.ITEM.key(), id("repairs_ruby"));

    // Between copper (190 uses, speed 5, +1 damage) and iron (250, 6, +2).
    public static final ToolMaterial RUBY = new ToolMaterial(
        BlockTags.INCORRECT_FOR_IRON_TOOL, 220, 5.5F, 1.5F, 12, REPAIRS_RUBY);

    public static final Item RAW_RUBY = item("raw_ruby", new Item.Properties());
    public static final Item RUBY_INGOT = item("ruby_ingot", new Item.Properties());

    private static final ResourceKey<Block> RUBY_ORE_KEY =
        ResourceKey.create(Registries.BLOCK, id("ruby_ore"));

    private static final Block RUBY_ORE = new Block(BlockBehaviour.Properties.of()
        .setId(RUBY_ORE_KEY)
        .mapColor(MapColor.COLOR_RED)
        .requiresCorrectToolForDrops()
        .strength(3.0F, 3.0F)
        .sound(SoundType.STONE));

    public static final Item RUBY_ORE_ITEM = blockItem("ruby_ore", RUBY_ORE);

    public static final Item RUBY_SWORD = tool("ruby_sword", properties -> properties.sword(RUBY, 3.0F, -2.4F));
    public static final Item RUBY_PICKAXE = tool("ruby_pickaxe", properties -> properties.pickaxe(RUBY, 1.0F, -2.8F));
    public static final Item RUBY_AXE = tool("ruby_axe", properties -> properties.axe(RUBY, 5.5F, -3.0F));
    public static final Item RUBY_SHOVEL = tool("ruby_shovel", properties -> properties.shovel(RUBY, 1.5F, -3.0F));
    public static final Item RUBY_HOE = tool("ruby_hoe", properties -> properties.hoe(RUBY, -2.0F, 0.0F));

    private static final ResourceKey<PlacedFeature> RUBY_ORE_PLACED =
        ResourceKey.create(Registries.PLACED_FEATURE, id("ruby_ore"));

    @Override
    public void onInitialize()
    {
        Registry.register(BuiltInRegistries.BLOCK, RUBY_ORE_KEY, RUBY_ORE);

        BiomeModifications.addFeature(BiomeSelectors.foundInOverworld(),
            GenerationStep.Decoration.UNDERGROUND_ORES, RUBY_ORE_PLACED);

        // Findable in the creative inventory, beside the material each thing belongs to.
        CreativeModeTabEvents.modifyOutputEvent(CreativeModeTabs.INGREDIENTS).register(output ->
        {
            output.accept(RAW_RUBY);
            output.accept(RUBY_INGOT);
        });
        CreativeModeTabEvents.modifyOutputEvent(CreativeModeTabs.NATURAL_BLOCKS).register(output ->
            output.accept(RUBY_ORE_ITEM));
        CreativeModeTabEvents.modifyOutputEvent(CreativeModeTabs.TOOLS_AND_UTILITIES).register(output ->
        {
            output.accept(RUBY_SWORD);
            output.accept(RUBY_PICKAXE);
            output.accept(RUBY_AXE);
            output.accept(RUBY_SHOVEL);
            output.accept(RUBY_HOE);
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
