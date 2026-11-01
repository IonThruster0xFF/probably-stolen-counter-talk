using Newtonsoft.Json.Linq;

namespace CounterTalk
{
    public static class ItemFacts
    {
        // Explicit scalar snapshots, never recursive serialization of live Unity/game objects.
        private static JObject Tags(TagSystem state)
        {
            var result = new JObject();
            if (state?.dict == null) return result;
            foreach (var pair in state.dict)
            {
                var tag = pair.Value;
                if (tag == null) continue;
                result[pair.Key] = new JObject {
                    ["enabled"] = tag.valueEnabled, ["label"] = tag.identifierName,
                    ["string"] = tag.valueString, ["int"] = tag.valueInt, ["long"] = tag.valueLong,
                    ["float"] = tag.valueFloat, ["double"] = tag.valueDouble, ["bool"] = tag.valueBool
                };
            }
            return result;
        }
        public static JObject Read(GameItem item)
        {
            var tags = new JArray();
            foreach (string tag in new[] { "LIQUID_CONTAINER_TAG", "GRADUATED_CONTAINER_TAG", "WINE_CONTAINER_TAG", "FILTER_CONTAINER_TAG" })
                if (item.IsTag(tag)) tags.Add(tag);
            var features = new JArray();
            var allFeatures = new JArray();
            foreach (var feature in item.itemFeatures)
            {
                allFeatures.Add(new JObject {
                    ["id"] = feature.identifier, ["category"] = feature.category,
                    ["public_label"] = DialogUIManager.RemoveTag(feature.GetPublicDisplay()),
                    ["actual_label"] = DialogUIManager.RemoveTag(feature.GetActualDisplay()),
                    ["revealed"] = feature.IsExposed(), ["initially_shown"] = feature.IsInitiallyShown(),
                    ["public_hidden"] = feature.GetPublicHidden(), ["disabled"] = feature.isDisabled
                });
                if (feature.isDisabled || !feature.IsInitiallyShown() || (feature.GetPublicHidden() && !feature.IsExposed())) continue;
                features.Add(new JObject {
                    ["label"] = DialogUIManager.RemoveTag(feature.IsExposed() ? feature.GetExposedString() : feature.GetPublicDisplay()),
                    ["revealed"] = feature.IsExposed()
                });
            }
            JObject liquid = null;
            if (item.IsTag("LIQUID_CONTAINER_TAG"))
            {
                // Use microliters before converting: the integer ML helper truncates residual liquid to zero.
                int current = WaterHelper.GetCurrentCapacityUL(item);
                int capacity = WaterHelper.GetTotalCapacity(item);
                liquid = new JObject {
                    ["current_ml"] = current / 1000m, ["capacity_ml"] = capacity / 1000m,
                    ["is_empty"] = WaterHelper.IsEmpty(item),
                    ["contents_state"] = WaterHelper.IsEmpty(item) ? "empty_container" : "contains_liquid",
                    ["composition"] = "not_inferred_from_container_name"
                };
            }
            return new JObject {
                ["base_tags"] = Tags(item.state), ["effective_tags"] = Tags(item.modifiedState),
                ["all_features"] = allFeatures, ["container_tags"] = tags, ["visible_features"] = features, ["liquid"] = liquid
            };
        }
    }
}
