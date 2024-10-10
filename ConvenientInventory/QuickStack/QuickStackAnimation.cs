﻿using System;
using System.Collections.Generic;
using ConvenientInventory.TypedChests;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Objects;

namespace ConvenientInventory.QuickStack
{
    /// <summary>
    /// Handles the creation and broadcasting of item sprites to be used in a quick stack animation where
    /// items are visually deposited into their respective chests.
    /// </summary>
    public class QuickStackAnimation
    {
        private readonly Random random = new();

        /// <summary>
        /// Creates a new instance of <see cref="QuickStackAnimation"/> with reference to the provided farmer.
        /// </summary>
        /// <param name="who">The farmer triggering this animation.</param>
        public QuickStackAnimation(Farmer who)
        {
            Farmer = who;
        }

        public int NumAnimatedItems { get; private set; }

        private Dictionary<GameLocation, TemporaryAnimatedSpriteList> ItemSpritesByLocation { get; } = new();

        private Farmer Farmer { get; }

        private Dictionary<TypedChest, int> NumAnimatedItemsByChest { get; } = new();

        /// <summary>
        /// Broadcasts item sprites to begin animation, synced with multiplayer.
        /// </summary>
        /// <param name="who">Farmer initiating the animation.</param>
        /// <returns>The total number of item stacks in the animation.</returns>
        public int Complete()
        {
            foreach (GameLocation location in ItemSpritesByLocation.Keys)
            {
                TemporaryAnimatedSpriteList locationItemSprites = ItemSpritesByLocation[location];
                Game1.Multiplayer.broadcastSprites(location, locationItemSprites);
            }

            return NumAnimatedItems;
        }

        /// <summary>
        /// Adds sprites to this <see cref="QuickStackAnimation"/> for <paramref name="item"/> to be visually quick stacked into <paramref name="typedChest"/>.
        /// </summary>
        /// <param name="typedChest">The chest to be quick stacked into.</param>
        /// <param name="item">The item being quick stacked.</param>
        public void AddToAnimation(TypedChest typedChest, Item item)
        {
            if (!NumAnimatedItemsByChest.TryGetValue(typedChest, out int numChestAnimatedItems))
            {
                numChestAnimatedItems = 0;
                NumAnimatedItemsByChest[typedChest] = 0;
            }

            Chest chest = typedChest.Chest;
            Vector2 chestTileLocation = typedChest.VisualTileLocation ?? chest.TileLocation;
            Vector2 chestPosition = (chestTileLocation + new Vector2(0, -1.5f)) * Game1.tileSize;

            Vector2 farmerOffset = Farmer.FacingDirection switch
            {
                0 => new Vector2(0f, -1.5f) * Game1.tileSize,   // Up
                1 => new Vector2(0.5f, -1f) * Game1.tileSize,   // Right
                3 => new Vector2(-0.5f, -1f) * Game1.tileSize,  // Left
                _ => new Vector2(0f, -1f) * Game1.tileSize,     // Down
            };
            Vector2 randFarmerOffset = new(random.NextSingle() * 16f - 8f, random.NextSingle() * 16f - 8f);
            Vector2 farmerPosition = Farmer.Position + farmerOffset + randFarmerOffset;

            float distance = Vector2.Distance(farmerPosition, chestPosition);
            Vector2 motionVec = (chestPosition - farmerPosition) * 0.98f; // 0.98 multiplier gives a better result in-game; without it, items slightly overshoot the chest.

            float baseLayerDepth = (float)((chestTileLocation.Y + 1) * 64) / 10000f + chestTileLocation.X / 50000f; // Refactored from Object.draw()
            float addlayerDepth = 1E-06f * NumAnimatedItems; // Avoids z-fighting by drawing each sprite on a separate layer depth.

            int hoverTimePerItem = (int)(150 / ModEntry.Config.QuickStackAnimationStackSpeed);
            int fadeTime = (int)(500 / ModEntry.Config.QuickStackAnimationStackSpeed);

            ParsedItemData itemData = ItemRegistry.GetDataOrErrorItem(item.QualifiedItemId);

            TemporaryAnimatedSprite itemTossSprite;
            bool isChestInCurrentLocation = typedChest.ChestGameLocation == Game1.currentLocation;
            if (isChestInCurrentLocation)
            {
                // "Item toss" animation is used if the chest is in the current GameLocation.
                float tossTime = ((float)(10 * Math.Pow(distance, 0.5)) + 400 - 0.5f * Math.Min(0, motionVec.Y)) / ModEntry.Config.QuickStackAnimationItemSpeed;

                float extraHeight = 192 - Math.Min(0, motionVec.Y);
                float gravity = 2 * extraHeight / tossTime;
                motionVec.Y -= extraHeight;
                float extraX = motionVec.X;
                float xAccel = -2 * motionVec.X / tossTime;
                motionVec.X += extraX;

                itemTossSprite = new(itemData.GetTextureName(), itemData.GetSourceRect(), farmerPosition, false, alphaFade: 0f, Color.White)
                {
                    scale = 4f,
                    layerDepth = 1f - addlayerDepth,
                    totalNumberOfLoops = 0,
                    interval = tossTime,
                    motion = motionVec / tossTime,
                    acceleration = new Vector2(xAccel, gravity) / tossTime,
                    timeBasedMotion = true,
                };
            }
            else
            {
                // "Upward item fade" animation is used if the chest is in a separate GameLocation.
                int upwardFadeTime = (int)(500 / ModEntry.Config.QuickStackAnimationItemSpeed);
                itemTossSprite = new(itemData.GetTextureName(), itemData.GetSourceRect(), farmerPosition, false, 0f, Color.White)
                {
                    scale = 4f,
                    layerDepth = 1f - addlayerDepth,
                    alphaFade = 0.04f * ModEntry.Config.QuickStackAnimationItemSpeed,
                    interval = upwardFadeTime,
                    motion = new Vector2(0.6f, -4.5f) * ModEntry.Config.QuickStackAnimationItemSpeed,
                    acceleration = new Vector2(0f, 0.08f) * ModEntry.Config.QuickStackAnimationItemSpeed,
                    scaleChange = -0.07f * ModEntry.Config.QuickStackAnimationItemSpeed,
                };
            }
            
            TemporaryAnimatedSprite itemHoverSprite = new(itemData.GetTextureName(), itemData.GetSourceRect(), chestPosition, false, alphaFade: 0f, Color.White)
            {
                delayBeforeAnimationStart = isChestInCurrentLocation ? itemTossSprite.delayBeforeAnimationStart + (int)itemTossSprite.interval : 0,
                scale = 4f,
                layerDepth = baseLayerDepth - addlayerDepth,
                interval = numChestAnimatedItems * hoverTimePerItem,
            };
            TemporaryAnimatedSprite itemFadeSprite = new(itemData.GetTextureName(), itemData.GetSourceRect(), chestPosition, false, 0f, Color.White)
            {
                delayBeforeAnimationStart = itemHoverSprite.delayBeforeAnimationStart + (int)itemHoverSprite.interval,
                scale = 4f,
                layerDepth = baseLayerDepth + addlayerDepth,
                alphaFade = 0.04f * ModEntry.Config.QuickStackAnimationStackSpeed,
                interval = fadeTime,
                motion = new Vector2(0.6f, 4.5f) * ModEntry.Config.QuickStackAnimationStackSpeed,
                acceleration = new Vector2(0f, -0.08f) * ModEntry.Config.QuickStackAnimationStackSpeed,
                scaleChange = -0.07f * ModEntry.Config.QuickStackAnimationStackSpeed,
            };

            // Animate sprites separately by game location.
            if (!ItemSpritesByLocation.ContainsKey(typedChest.ChestGameLocation))
            {
                ItemSpritesByLocation.Add(typedChest.ChestGameLocation, new TemporaryAnimatedSpriteList());
            }

            if (isChestInCurrentLocation)
            {
                ItemSpritesByLocation[typedChest.ChestGameLocation].Add(itemTossSprite);
            }
            else
            {
                // Handle "upward item fade" animation in current GameLocation if chest is in a separate location.
                if (!ItemSpritesByLocation.ContainsKey(Game1.currentLocation))
                {
                    ItemSpritesByLocation.Add(Game1.currentLocation, new TemporaryAnimatedSpriteList());
                }

                ItemSpritesByLocation[Game1.currentLocation].Add(itemTossSprite);
            }

            ItemSpritesByLocation[typedChest.ChestGameLocation].Add(itemHoverSprite);
            ItemSpritesByLocation[typedChest.ChestGameLocation].Add(itemFadeSprite);

            NumAnimatedItems++;
            NumAnimatedItemsByChest[typedChest]++;

            int totalAnimationTimeMs = itemFadeSprite.delayBeforeAnimationStart + (int)itemFadeSprite.interval;
            QuickStackChestAnimation.SetModData(chest, totalAnimationTimeMs);
        }
    }
}
