"use client";

import { NAVIGATION_TIPS, navigationHref } from "@/lib/assistant";
import { Actions, AssistantCard, GoLink } from "./shared";

export function NavigateCard({ navigation }: { navigation: { key: string; label: string; path: string } }) {
  const tips = NAVIGATION_TIPS[navigation.key];
  return (
    <AssistantCard>
      <p className="text-sm font-bold">{navigation.label}</p>
      {tips && <ol className="list-decimal space-y-1 pl-5 text-sm">{tips.map((tip) => <li key={tip}>{tip}</li>)}</ol>}
      <Actions><GoLink href={navigationHref(navigation.key, navigation.path)}>{navigation.label} ekranına git</GoLink></Actions>
    </AssistantCard>
  );
}
