import { tv } from "@/lib/utils";

// The site's pagination styles.
export const pagination = tv({
  base: "mx-auto flex w-full justify-center",
});

export const paginationContent = tv({
  base: "flex flex-wrap items-center justify-center gap-0 sm:gap-5",
});

export const paginationEllipsis = tv({
  base: "flex h-34 w-13 items-center justify-center max-[359px]:hidden sm:size-34 [&_svg:not([class*='size-'])]:size-13",
});
