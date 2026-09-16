import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Tasarım Alternatifleri",
  description: "Abdera Sanat Akademi için üç kurumsal web sitesi tasarım alternatifi.",
};

export default function DesignLabLayout({ children }: LayoutProps<"/tasarimlar">) {
  return children;
}
