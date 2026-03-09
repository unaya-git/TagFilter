using System.Collections.Generic;
using System.Linq;

namespace TagFilter
{
    public class TagClassifier
    {
        private static readonly Dictionary<BodyPartCategory, string[]> CategoryKeywords
            = new Dictionary<BodyPartCategory, string[]>
        {
        {
            BodyPartCategory.Face, new[]
            {
                "eyes", "eye", "pupils", "iris", "eyebrows", "eyelashes",
                "nose", "mouth", "lips", "teeth", "tongue", "ear", "ears",
                "face", "cheeks", "forehead", "chin", "blush", "tears",
                "smile", "expression", "looking", "gaze", "closed_eyes",
                "hair", "hairband", "ahoge", "twintails", "ponytail",
                "makeup", "eyeshadow", "eyeliner", "lipstick"
            }
        },
        {
            BodyPartCategory.Body, new[]
            {
                "breasts", "chest", "stomach", "navel", "abs",
                "shoulders", "arms", "hands", "fingers", "legs", "thighs",
                "feet", "toes", "back", "spine", "waist", "hips",
                "skin", "body", "neck", "collarbone", "cleavage"
            }
        },
        {
            BodyPartCategory.Outfit, new[]
            {
                "dress", "skirt", "shirt", "jacket", "coat", "uniform",
                "swimsuit", "bikini", "bra", "panties", "underwear",
                "pants", "shorts", "socks", "stockings", "gloves", "hat",
                "ribbon", "bow", "collar", "tie", "scarf", "cape",
                "clothes", "clothing", "outfit", "costume", "maid"
            }
        },
        {
            BodyPartCategory.Pose, new[]
            {
                "sitting", "standing", "lying", "kneeling", "crouching",
                "arms_up", "arms_behind", "hand_on_hip", "crossed_arms",
                "spread_legs", "leaning", "bending", "walking", "running",
                "pose", "posture", "from_above", "from_below", "from_behind",
                "portrait", "full_body", "upper_body", "close-up"
            }
        },
        {
            BodyPartCategory.Background, new[]
            {
                "background", "sky", "clouds", "indoors", "outdoors",
                "forest", "city", "room", "bedroom", "school", "beach",
                "floor", "wall", "window", "grass", "nature",
                "simple_background", "white_background", "gradient_background"
            }
        },
        {
            BodyPartCategory.Style, new[]
            {
                "highres", "absurdres", "masterpiece", "best_quality",
                "realistic", "anime", "illustration", "digital_art",
                "watercolor", "sketch", "monochrome", "colorful",
                "detailed", "sharp", "soft_focus", "bokeh"
            }
        }
        };

        public BodyPartCategory Classify(string tagName)
        {
            var normalized = tagName.ToLower().Replace(" ", "_");
            foreach (var pair in CategoryKeywords)
            {
                var category = pair.Key;
                var keywords = pair.Value;
                if (keywords.Any(kw =>
                    normalized == kw ||
                    normalized.Contains("_" + kw) ||
                    normalized.Contains(kw + "_") ||
                    normalized.StartsWith(kw) ||
                    normalized.EndsWith(kw)))
                {
                    return category;
                }
            }
            return BodyPartCategory.Other;
        }

        public List<string> FilterForFaceLora(IEnumerable<string> tags)
        {
            return tags.Where(t => Classify(t) == BodyPartCategory.Face).ToList();
        }

        public List<string> FilterByCategories(
            IEnumerable<string> tags,
            IEnumerable<BodyPartCategory> keepCategories)
        {
            var keepSet = new HashSet<BodyPartCategory>(keepCategories);
            return tags.Where(t => keepSet.Contains(Classify(t))).ToList();
        }
    }

    public enum BodyPartCategory
    {
        Face,
        Body,
        Outfit,
        Pose,
        Background,
        Style,
        Other
    }
}