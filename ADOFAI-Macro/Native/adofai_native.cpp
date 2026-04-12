#include <cstddef>

#if defined(_WIN32)
#define ADOFAI_EXPORT __declspec(dllexport)
#else
#define ADOFAI_EXPORT __attribute__((visibility("default")))
#endif

extern "C" {
    ADOFAI_EXPORT int generate_delay_table(
        const double* note_times_ms,
        int count,
        double global_offset_ms,
        double* output)
    {
        if (count < 0 || note_times_ms == nullptr || output == nullptr)
        {
            return -1;
        }

        for (int i = 0; i < count; ++i)
        {
            output[i] = note_times_ms[i] + global_offset_ms;
        }

        return 0;
    }

    ADOFAI_EXPORT int resolve_key_counts(
        const int* tile_indices,
        int notes_count,
        const int* range_start_tile_indices,
        const int* range_key_counts,
        int range_count,
        int default_key_count,
        int* output)
    {
        if (notes_count < 0 || range_count < 0 || tile_indices == nullptr || output == nullptr)
        {
            return -1;
        }

        if (range_count > 0 && (range_start_tile_indices == nullptr || range_key_counts == nullptr))
        {
            return -2;
        }

        int range_index = 0;
        int current_key_count = default_key_count;

        for (int i = 0; i < notes_count; ++i)
        {
            while (range_index < range_count && range_start_tile_indices[range_index] <= tile_indices[i])
            {
                current_key_count = range_key_counts[range_index];
                ++range_index;
            }

            output[i] = current_key_count;
        }

        return 0;
    }
}