using transformers.utils;

namespace mingpt3;

/**
 * https://chatgpt.com/share/67084674-24ec-8009-b7e1-6e45002c5f96
 */
public class Model
{
    public int VocabSize, EmbeddingSize, NumHeads, NumLayers, MaxSeqLen;
    public EmbeddingLayer TokenEmbedding;
    public EmbeddingLayer PositionalEmbedding;
    public TransformerBlock[] Layers;
    public LinearLayer FinalLayer;

    public Model (int vocabSize, int embeddingSize, int numHeads, int numLayers, int maxSeqLen) {
        VocabSize = vocabSize;
        EmbeddingSize = embeddingSize;
        NumHeads = numHeads;
        NumLayers = numLayers;
        MaxSeqLen = maxSeqLen;

        TokenEmbedding = new EmbeddingLayer (vocabSize, embeddingSize);
        PositionalEmbedding = new EmbeddingLayer (maxSeqLen, embeddingSize);
        Layers = new TransformerBlock[numLayers];
        for (int i = 0; i < numLayers; i++)
            Layers[i] = new TransformerBlock (embeddingSize, numHeads);
        FinalLayer = new LinearLayer (embeddingSize, vocabSize);
    }

    public Matrix[] Forward (int[][] batchInputIds) {
        int batchSize = batchInputIds.Length;
        Matrix[] x = new Matrix[batchSize];

        // Token and positional embeddings
        for (int b = 0; b < batchSize; b++) {
            var tokenEmb = TokenEmbedding.Forward (batchInputIds[b]);
            var positions = new int[batchInputIds[b].Length];
            for (int i = 0; i < positions.Length; i++)
                positions[i] = i;
            var posEmb = PositionalEmbedding.Forward (positions);
            x[b] = tokenEmb + posEmb;
        }

        // Transformer layers
        foreach (var layer in Layers) {
            for (int b = 0; b < batchSize; b++)
                x[b] = layer.Forward (x[b]);
        }

        // Final linear layer
        for (int b = 0; b < batchSize; b++) {
            x[b] = FinalLayer.Forward (x[b]);
        }

        return x; // Returns an array of matrices representing logits for each batch item
    }

    public void Backward (Matrix[] dLogits, int[][] batchInputIds) {
        int batchSize = dLogits.Length;
        Matrix[] dX = new Matrix[batchSize];

        // Backward through final linear layer
        for (int b = 0; b < batchSize; b++)
            dX[b] = FinalLayer.Backward (dLogits[b]);

        // Backward through transformer layers
        for (int i = Layers.Length - 1; i >= 0; i--) {
            for (int b = 0; b < batchSize; b++)
                dX[b] = Layers[i].Backward (dX[b]);
        }

        // Backward through embeddings
        for (int b = 0; b < batchSize; b++) {
            Matrix dPosEmb = dX[b];
            TokenEmbedding.Backward (dX[b], batchInputIds[b]);
            PositionalEmbedding.Backward (dPosEmb, GetPositions (batchInputIds[b].Length));
        }
    }

    private int[] GetPositions (int length) {
        var positions = new int[length];
        for (int i = 0; i < length; i++)
            positions[i] = i;
        return positions;
    }

    private double[] Softmax (double[] logits) {
        double maxLogit = double.NegativeInfinity;
        for (int i = 0; i < logits.Length; i++)
            if (logits[i] > maxLogit)
                maxLogit = logits[i];

        double sumExp = 0.0;
        var expLogits = new double[logits.Length];
        for (int i = 0; i < logits.Length; i++) {
            expLogits[i] = Math.Exp (logits[i] - maxLogit);
            sumExp += expLogits[i];
        }

        var probabilities = new double[logits.Length];
        for (int i = 0; i < logits.Length; i++)
            probabilities[i] = expLogits[i] / sumExp;

        return probabilities;
    }

    public int PredictNextToken (int[] inputIds, double temperature = 1.0, int topK = 0, bool argmax = false) {
        var logits = Forward (new int[][] { inputIds })[0];
        int lastPosition = inputIds.Length - 1;
        var lastLogits = new double[VocabSize];
        for (int i = 0; i < VocabSize; i++)
            lastLogits[i] = logits.Data[lastPosition, i] / temperature;

        if (topK > 0) {
            lastLogits = TopKFilter (lastLogits, topK);
        }

        // Apply softmax to convert logits to probabilities
        var probabilities = Softmax (lastLogits);

        // Sample the next token based on probabilities
        return argmax
            ? sampling.ArgMax (probabilities)
            : sampling.SampleFromDistribution (probabilities);
    }

    private double[] TopKFilter (double[] logits, int k) {
        var filteredLogits = new double[logits.Length];
        Array.Copy (logits, filteredLogits, logits.Length);

        var indices = new int[logits.Length];
        for (int i = 0; i < logits.Length; i++)
            indices[i] = i;

        Array.Sort (logits, indices);
        Array.Reverse (logits);
        Array.Reverse (indices);

        for (int i = k; i < logits.Length; i++)
            filteredLogits[indices[i]] = double.NegativeInfinity;

        return filteredLogits;
    }
}
