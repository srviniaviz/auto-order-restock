using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using Photon.Pun;
using UnityEngine;

namespace AutoOrderRestock;

[BepInPlugin("vinicius.autoorderrestock", "Auto Order Restock", "0.5.2")]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource LogSource;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> SalesShelvesEnabled;
    internal static ConfigEntry<bool> StorageRacksEnabled;
    internal static ConfigEntry<bool> DryRun;
    internal static ConfigEntry<int> BoxesPerProduct;
    internal static ConfigEntry<float> CheckIntervalSeconds;
    internal static ConfigEntry<int> MaxOrdersPerSession;
    internal static ConfigFile Settings;

    public override void Load()
    {
        LogSource = Log;
        Settings = Config;
        Enabled = Config.Bind("General", "Enabled", true, "Automatically order products when a display or labeled storage rack slot is incomplete and no reserve stock remains.");
        SalesShelvesEnabled = Config.Bind("General", "SalesShelvesEnabled", true, "Include incomplete sales shelves when deciding what to order.");
        StorageRacksEnabled = Config.Bind("General", "StorageRacksEnabled", true, "Include incomplete labeled storage rack slots when deciding what to order.");
        DryRun = Config.Bind("General", "DryRun", false, "Log planned orders without spending money.");
        BoxesPerProduct = Config.Bind("General", "BoxesPerProduct", 1, "Boxes to order per product, from 1 to 10.");
        CheckIntervalSeconds = Config.Bind("General", "CheckIntervalSeconds", 10f, "Seconds between checks, minimum 5.");
        MaxOrdersPerSession = Config.Bind("General", "MaxOrdersPerSession", 0, "Safety limit for orders in the current game session; 0 means unlimited.");
        ClassInjector.RegisterTypeInIl2Cpp<AutoOrderBehaviour>();
        var obj = new GameObject("Auto Order Restock");
        UnityEngine.Object.DontDestroyOnLoad(obj);
        obj.AddComponent<AutoOrderBehaviour>();
        Log.LogInfo($"Loaded. DryRun={DryRun.Value}, MaxOrdersPerSession={MaxOrdersPerSession.Value}.");
    }
}

public sealed class AutoOrderBehaviour : MonoBehaviour
{
    private readonly HashSet<int> _pending = new();
    private readonly HashSet<int> _logged = new();
    private readonly Dictionary<int, float> _retryAfter = new();
    private readonly Dictionary<int, int> _boxesAtOrder = new();
    private readonly Dictionary<int, int> _inventoryAtOrder = new();
    private float _nextScan;
    private float _noticeUntil;
    private string _notice = "";
    private int _ordersThisSession;
    private int _lastRackSlotCount = -1;
    private int _lastRackNeedCount = -1;

    public AutoOrderBehaviour(IntPtr pointer) : base(pointer) { }

    private void Update()
    {
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
            {
                if (Input.GetKeyDown(KeyCode.R))
                {
                    Plugin.Enabled.Value = !Plugin.Enabled.Value;
                    Notify(Plugin.Enabled.Value ? "Pedidos automáticos: ligados" : "Pedidos automáticos: pausados");
                    Plugin.LogSource.LogInfo($"Enabled={Plugin.Enabled.Value}");
                }
                if (Input.GetKeyDown(KeyCode.S))
                {
                    Plugin.SalesShelvesEnabled.Value = !Plugin.SalesShelvesEnabled.Value;
                    Notify(Plugin.SalesShelvesEnabled.Value ? "Prateleiras de venda: ligadas" : "Prateleiras de venda: pausadas");
                    Plugin.LogSource.LogInfo($"SalesShelvesEnabled={Plugin.SalesShelvesEnabled.Value}");
                }
                if (Input.GetKeyDown(KeyCode.E))
                {
                    Plugin.StorageRacksEnabled.Value = !Plugin.StorageRacksEnabled.Value;
                    Notify(Plugin.StorageRacksEnabled.Value ? "Estantes do estoque: ligadas" : "Estantes do estoque: pausadas");
                    Plugin.LogSource.LogInfo($"StorageRacksEnabled={Plugin.StorageRacksEnabled.Value}");
                }
                if (Input.GetKeyDown(KeyCode.L))
                {
                    Plugin.Settings.Reload();
                    Notify($"Config recarregada: {Plugin.BoxesPerProduct.Value} caixas");
                    Plugin.LogSource.LogInfo($"Config reloaded: DryRun={Plugin.DryRun.Value}, MaxOrdersPerSession={Plugin.MaxOrdersPerSession.Value}, SalesShelvesEnabled={Plugin.SalesShelvesEnabled.Value}, StorageRacksEnabled={Plugin.StorageRacksEnabled.Value}.");
                }
            }
        }

        if (!Plugin.Enabled.Value || (!Plugin.SalesShelvesEnabled.Value && !Plugin.StorageRacksEnabled.Value) || Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + Math.Max(5f, Plugin.CheckIntervalSeconds.Value);
        try { Scan(); }
        catch (Exception ex) { Plugin.LogSource.LogError($"Restock scan failed: {ex}"); }
    }

    private void Scan()
    {
        if (PhotonNetwork.InRoom) return;
        var displays = DisplayManager.Instance;
        var inventory = InventoryManager.Instance;
        var idManager = IDManager.Instance;
        var licenses = ProductLicenseManager.Instance;
        var cartManager = CartManager.Instance;
        var cart = cartManager?.MarketShoppingCart;
        if (displays == null || inventory == null || idManager == null || licenses == null || cart == null || displays.DisplayedProducts == null) return;
        if (cart.TooLateToOrderGoods) return;

        if (cart.ItemCountInCart != 0 || HasAnythingInCart(cart)) return;

        var eligible = new List<ProductSO>();
        var productsInBoxes = new HashSet<int>();
        var looseBoxes = new Dictionary<int, int>();
        var physicalBoxes = new Dictionary<int, int>();
        var observedInventory = new Dictionary<int, int>();
        foreach (var box in UnityEngine.Object.FindObjectsOfType<Box>())
        {
            if (box != null && box.Data != null && box.ProductCount > 0 && box.Data.ProductID > 0)
            {
                productsInBoxes.Add(box.Data.ProductID);
                physicalBoxes.TryGetValue(box.Data.ProductID, out var totalBoxes);
                physicalBoxes[box.Data.ProductID] = totalBoxes + 1;
                if (!box.Racked)
                {
                    looseBoxes.TryGetValue(box.Data.ProductID, out var count);
                    looseBoxes[box.Data.ProductID] = count + 1;
                }
            }
        }
        var incompleteRackSlots = new Dictionary<int, int>();
        var rackSlotCount = 0;
        if (Plugin.StorageRacksEnabled.Value) foreach (var slot in UnityEngine.Object.FindObjectsOfType<RackSlot>())
        {
            rackSlotCount++;
            if (slot != null && slot.Data != null && slot.Data.ProductID > 0 && !slot.Full)
            {
                incompleteRackSlots.TryGetValue(slot.Data.ProductID, out var count);
                incompleteRackSlots[slot.Data.ProductID] = count + 1;
            }
        }
        if (rackSlotCount != _lastRackSlotCount || incompleteRackSlots.Count != _lastRackNeedCount)
        {
            Plugin.LogSource.LogInfo($"Rack scan: {rackSlotCount} slots, {incompleteRackSlots.Count} products with incomplete labeled rack slots.");
            _lastRackSlotCount = rackSlotCount;
            _lastRackNeedCount = incompleteRackSlots.Count;
        }

        var ids = new HashSet<int>(incompleteRackSlots.Keys);
        if (Plugin.SalesShelvesEnabled.Value) foreach (var id in displays.DisplayedProducts.Keys) ids.Add(id);
        foreach (var id in ids)
        {
            if (id <= 0) continue;
            try
            {
                displays.DisplayedProducts.TryGetValue(id, out var slots);
                var shelfNeedsStock = false;
                if (Plugin.SalesShelvesEnabled.Value && slots != null) foreach (var slot in slots)
                {
                    if (slot != null &&
                        (slot.HasProduct ? slot.ProductID : slot.Data != null && slot.Data.HasLabel ? slot.Data.FirstItemID : -1) == id &&
                        !slot.Full)
                    {
                        shelfNeedsStock = true;
                        break;
                    }
                }
                incompleteRackSlots.TryGetValue(id, out var rackSlotsNeedingBoxes);
                looseBoxes.TryGetValue(id, out var looseBoxCount);
                var rackNeedsStock = rackSlotsNeedingBoxes > looseBoxCount;
                if (!shelfNeedsStock && !rackNeedsStock)
                {
                    ClearPending(id);
                    continue;
                }
                var shown = displays.GetDisplayedProductCount(id);
                var total = inventory.GetInventoryAmount(id);
                observedInventory[id] = total;
                var reserve = Math.Max(0, total - shown);
                if (!rackNeedsStock && (reserve > 0 || productsInBoxes.Contains(id)))
                {
                    ClearPending(id);
                    continue;
                }
                if (_logged.Add(id))
                    Plugin.LogSource.LogInfo($"Shortage id={id}: displaySlots={slots?.Count ?? 0}, displayNeedsStock={shelfNeedsStock}, incompleteRackSlots={rackSlotsNeedingBoxes}, looseBoxes={looseBoxCount}, rackNeedsStock={rackNeedsStock}, displayed={shown}, inventory={total}, reserve={reserve}, pending={_pending.Contains(id)}");
                physicalBoxes.TryGetValue(id, out var currentBoxes);
                if (_pending.Contains(id) &&
                    ((_boxesAtOrder.TryGetValue(id, out var boxesBefore) && currentBoxes > boxesBefore) ||
                     (_inventoryAtOrder.TryGetValue(id, out var inventoryBefore) && total > inventoryBefore)))
                {
                    ClearPending(id);
                    Plugin.LogSource.LogInfo($"Delivery detected for product {id}; remaining rack slots can be ordered again.");
                }
                if (_retryAfter.TryGetValue(id, out var retry))
                {
                    if (Time.unscaledTime < retry) continue;
                    ClearPending(id);
                }
                if (_pending.Contains(id)) continue;
                var product = idManager.ProductSO(id);
                if (product == null || product.IsHiddenInMarketApp || !licenses.IsProductLicenseActive(id)) continue;
                eligible.Add(product);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"Could not sample product {id}: {ex.Message}");
            }
        }

        if (eligible.Count == 0 || Plugin.MaxOrdersPerSession.Value > 0 && _ordersThisSession >= Plugin.MaxOrdersPerSession.Value) return;
        if (Plugin.DryRun.Value)
        {
            Plugin.LogSource.LogInfo($"DRY RUN: would order {Math.Clamp(Plugin.BoxesPerProduct.Value, 1, 10)} boxes each for {eligible.Count} products.");
            return;
        }
        TryOrderBatch(cart, eligible, physicalBoxes, observedInventory);
    }

    private static bool HasAnythingInCart(MarketShoppingCart cart)
    {
        var data = cart.CartData;
        return data == null ||
               (data.ProductInCarts != null && data.ProductInCarts.Count > 0) ||
               (data.FurnituresInCarts != null && data.FurnituresInCarts.Count > 0) ||
               (data.BucketsInCarts != null && data.BucketsInCarts.Count > 0) ||
               (data.FloorBoxesInCarts != null && data.FloorBoxesInCarts.Count > 0);
    }

    private void TryOrderBatch(MarketShoppingCart cart, List<ProductSO> products, Dictionary<int, int> physicalBoxes, Dictionary<int, int> observedInventory)
    {
        var count = Math.Clamp(Plugin.BoxesPerProduct.Value, 1, 10);
        var added = new List<int>();
        var expectedBoxes = 0;
        foreach (var product in products)
        {
            var id = product.ID;
            var item = new ItemQuantity(id, product.BasePrice);
            item.FirstItemCount = count;
            if (!cart.TryAddProduct(item, SalesType.PRODUCT))
            {
                Plugin.LogSource.LogInfo($"Cart limit reached or product {id} was rejected after adding {added.Count} products.");
                if (added.Count == 0) _retryAfter[id] = Time.unscaledTime + 120f;
                break;
            }
            added.Add(id);
            expectedBoxes += count;
            if (cart.GetAllProductBoxCountInCart() != expectedBoxes)
            {
                cart.CleanCart();
                Plugin.LogSource.LogWarning($"Cart did not keep {count} boxes per product; order cancelled.");
                return;
            }
        }

        if (added.Count == 0) return;
        cart.UpdateTotalPrice();
        if (cart.GetTotalPrice() <= 0f)
        {
            cart.CleanCart();
            Plugin.LogSource.LogWarning("Cart rejected the batch price; order cancelled.");
            return;
        }
        if (!cart.GetHasMoneyForPurchase())
        {
            cart.CleanCart();
            Plugin.LogSource.LogWarning($"Insufficient funds for {count} boxes each for {added.Count} products.");
            return;
        }
        cart.Purchase(false);
        foreach (var id in added)
        {
            _pending.Add(id);
            _retryAfter[id] = Time.unscaledTime + 120f;
            _boxesAtOrder[id] = physicalBoxes.TryGetValue(id, out var boxes) ? boxes : 0;
            _inventoryAtOrder[id] = observedInventory.TryGetValue(id, out var stock) ? stock : 0;
        }
        _ordersThisSession++;
        Plugin.LogSource.LogInfo($"Submitted {count} boxes each for {added.Count} products ({expectedBoxes} boxes total) through the game's market cart: {string.Join(",", added)}.");
        Notify($"Pedido enviado: {count} caixas para {added.Count} produtos");
    }

    private void Notify(string text)
    {
        _notice = text;
        _noticeUntil = Time.unscaledTime + 5f;
    }

    private void ClearPending(int id)
    {
        _pending.Remove(id);
        _retryAfter.Remove(id);
        _boxesAtOrder.Remove(id);
        _inventoryAtOrder.Remove(id);
    }

    private void OnGUI()
    {
        if (Time.unscaledTime < _noticeUntil)
            GUI.Box(new Rect(Screen.width - 310, 112, 295, 34), _notice);
    }
}
