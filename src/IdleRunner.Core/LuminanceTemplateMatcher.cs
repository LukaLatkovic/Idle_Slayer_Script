namespace IdleRunner.Core;

public static class LuminanceTemplateMatcher
{
    private const double VarianceEpsilon = 0.000001;

    public static double Compare(
        GameFrame frame,
        int left,
        int top,
        LuminanceTemplate template)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (template is null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        FrameRectangle candidateRegion = new(left, top, template.Width, template.Height);
        if (!candidateRegion.FitsWithin(frame.Width, frame.Height))
        {
            throw new ArgumentOutOfRangeException(nameof(left), "The candidate region must fit inside the frame.");
        }

        int sampleCount = checked(template.Width * template.Height);
        double templateSum = 0;
        double candidateSum = 0;

        for (int y = 0; y < template.Height; y++)
        {
            for (int x = 0; x < template.Width; x++)
            {
                templateSum += template.GetLuminance(x, y);
                candidateSum += frame.GetLuminance(left + x, top + y);
            }
        }

        double templateMean = templateSum / sampleCount;
        double candidateMean = candidateSum / sampleCount;
        double covariance = 0;
        double templateVariance = 0;
        double candidateVariance = 0;

        for (int y = 0; y < template.Height; y++)
        {
            for (int x = 0; x < template.Width; x++)
            {
                double templateDelta = template.GetLuminance(x, y) - templateMean;
                double candidateDelta = frame.GetLuminance(left + x, top + y) - candidateMean;
                covariance += templateDelta * candidateDelta;
                templateVariance += templateDelta * templateDelta;
                candidateVariance += candidateDelta * candidateDelta;
            }
        }

        if (templateVariance <= VarianceEpsilon || candidateVariance <= VarianceEpsilon)
        {
            return 0;
        }

        double correlation = covariance / Math.Sqrt(templateVariance * candidateVariance);
        return Math.Max(0, Math.Min(1, correlation));
    }

    public static TemplateMatchResult FindBest(
        GameFrame frame,
        FrameRectangle searchRegion,
        LuminanceTemplate template,
        int step = 1)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (template is null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        if (step <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }

        if (!searchRegion.FitsWithin(frame.Width, frame.Height)
            || template.Width > searchRegion.Width
            || template.Height > searchRegion.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(searchRegion));
        }

        int lastLeft = searchRegion.Right - template.Width;
        int lastTop = searchRegion.Bottom - template.Height;
        TemplateMatchResult best = new(searchRegion.Left, searchRegion.Top, 0);

        for (int top = searchRegion.Top; top <= lastTop; top += step)
        {
            for (int left = searchRegion.Left; left <= lastLeft; left += step)
            {
                double score = Compare(frame, left, top, template);
                if (score > best.Score)
                {
                    best = new TemplateMatchResult(left, top, score);
                }
            }
        }

        return best;
    }
}
