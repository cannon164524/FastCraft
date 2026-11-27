package dev.fastcraft;

import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.biome.v1.BiomeModifications;
import net.fabricmc.fabric.api.biome.v1.BiomeSelectors;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceKey;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.tags.BlockTags;
import net.minecraft.world.item.BlockItem;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ToolMaterial;
import net.minecraft.world.item.crafting.Ingredient;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.SoundType;
import net.minecraft.world.level.block.state.BlockBehaviour;
import net.minecraft.world.level.levelgen.GenerationStep;
import net.minecraft.world.level.levelgen.placement.PlacedFeature;
import net.minecraft.world.level.material.MapColor;

/**
 * The FastCraft amber tier. Everything here is vanilla's own API: a block, three items,
 * five tools built from a tool material, and one placed feature added to the overworld.
 * The textures and the names come from the FastCraft resource pack, which the launcher
 * keeps switched on, so this jar carries the content and the pack carries the look.
 */
public class FastCraftMod implements ModInitializer
{
    public static final String ID = "fastcraft";

    public static final Item RAW_AMBER = new Item(new Item.Properties());
    public static final Item AMBER_INGOT = new Item(new Item.Properties());

    // Between copper (190 uses, speed 5, damage 1) and iron (250, speed 6, damage 2).
    public static final ToolMaterial AMBER = new ToolMaterial(
        BlockTags.INCORRECT_FOR_IRON_TOOL, 220, 5.5F, 1.5F, 12, Ingredient.of(AMBER_INGOT));

    public static final Block AMBER_ORE = new Block(BlockBehaviour.Properties.of()
        .mapColor(MapColor.COLOR_ORANGE)
        .requiresCorrectToolForDrops()
        .strength(3.0F, 3.0F)
        .sound(SoundType.STONE));

    public static final ResourceKey<PlacedFeature> AMBER_ORE_PLACED = ResourceKey.create(
        Registries.PLACED_FEATURE, id("amber_ore"));

    @Override
    public void onInitialize()
    {
        register("raw_amber", RAW_AMBER);
        register("amber_ingot", AMBER_INGOT);

        Registry.register(BuiltInRegistries.BLOCK, id("amber_ore"), AMBER_ORE);
        Registry.register(BuiltInRegistries.ITEM, id("amber_ore"), new BlockItem(AMBER_ORE, new Item.Properties()));

        registerTool("amber_sword", properties -> properties.sword(AMBER, 3.0F, -2.4F));
        registerTool("amber_pickaxe", properties -> properties.pickaxe(AMBER, 1.0F, -2.8F));
        registerTool("amber_axe", properties -> properties.axe(AMBER, 5.5F, -3.0F));
        registerTool("amber_shovel", properties -> properties.shovel(AMBER, 1.5F, -3.0F));
        registerTool("amber_hoe", properties -> properties.hoe(AMBER, -2.0F, 0.0F));

        BiomeModifications.addFeature(
            BiomeSelectors.foundInOverworld(),
            GenerationStep.Decoration.UNDERGROUND_ORES,
            AMBER_ORE_PLACED);
    }

    private static void register(String name, Item item)
    {
        Registry.register(BuiltInRegistries.ITEM, id(name), item);
    }

    private static void registerTool(String name, java.util.function.UnaryOperator<Item.Properties> shape)
    {
        var properties = new Item.Properties();
        register(name, new Item(shape.apply(properties)));
    }

    public static ResourceLocation id(String path)
    {
        return ResourceLocation.fromNamespaceAndPath(ID, path);
    }
}
