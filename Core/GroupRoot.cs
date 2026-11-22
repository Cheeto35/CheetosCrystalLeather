namespace CrystalLeather
{
    // Works out which object a glowing piece's light belongs to, so a bag made of 9 pieces gets one light
    // instead of 9. Walk up from the piece and keep the highest item we find, but stop at a chunk (every loose
    // item sits in one) or a player (a bag you're wearing keeps its own light).
    public static class GroupRoot
    {
        public struct Node
        {
            public readonly bool IsEntity, IsChunk, IsPlayer;
            public Node(bool isEntity, bool isChunk, bool isPlayer) { IsEntity = isEntity; IsChunk = isChunk; IsPlayer = isPlayer; }
        }

        // ancestors[0] is the piece itself. Returns the index of the one that owns the light.
        public static int Pick(Node[] ancestors)
        {
            int root = 0;
            for (int i = 0; i < ancestors.Length; i++)
            {
                Node n = ancestors[i];
                if (n.IsChunk || n.IsPlayer) break;
                if (n.IsEntity) root = i;
            }
            return root;
        }
    }
}
